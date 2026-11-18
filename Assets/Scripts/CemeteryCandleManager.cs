using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Single NetworkObject manager coordinating up to 64 animated CemeteryCandles via
/// a synced ulong bitmask. Supports player relighting and ghost blowout events.
/// </summary>
public class CemeteryCandleManager : NetworkBehaviour
{
    public static CemeteryCandleManager Instance { get; private set; }

    private NetworkVariable<ulong> netLitMask = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private ulong localLitMask = ulong.MaxValue;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        localLitMask = ulong.MaxValue;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            netLitMask.Value = ulong.MaxValue;
        }
        netLitMask.OnValueChanged += OnLitMaskChanged;
        ApplyMaskToSceneCandles(netLitMask.Value);
    }

    public override void OnNetworkDespawn()
    {
        netLitMask.OnValueChanged -= OnLitMaskChanged;
        base.OnNetworkDespawn();
    }

    private void OnLitMaskChanged(ulong _, ulong newMask)
    {
        ApplyMaskToSceneCandles(newMask);
    }

    public bool IsCandleLit(int candleId)
    {
        if (candleId < 0 || candleId >= 64) return true;
        ulong mask = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
            ? netLitMask.Value
            : localLitMask;
        return (mask & (1UL << candleId)) != 0UL;
    }

    public void RequestSetCandleLit(int candleId, bool lit)
    {
        if (candleId < 0 || candleId >= 64) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            SetCandleLitServerRpc(candleId, lit);
        }
        else
        {
            SetCandleLit(candleId, lit);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetCandleLitServerRpc(int candleId, bool lit, RpcParams rpcParams = default)
    {
        if (PlayerController.TryGetByClientId(rpcParams.Receive.SenderClientId, out var sender) && !sender.IsAlive)
        {
            return;
        }
        SetCandleLit(candleId, lit);
    }

    public void SetCandleLit(int candleId, bool lit)
    {
        if (candleId < 0 || candleId >= 64) return;
        ulong bit = 1UL << candleId;

        bool usingNet = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        if (usingNet)
        {
            if (!IsServer) return;
            ulong next = lit ? (netLitMask.Value | bit) : (netLitMask.Value & ~bit);
            if (next != netLitMask.Value)
            {
                netLitMask.Value = next;
                PlayCandleToggleFeedback(candleId);
            }
        }
        else
        {
            ulong next = lit ? (localLitMask | bit) : (localLitMask & ~bit);
            if (next != localLitMask)
            {
                localLitMask = next;
                ApplyMaskToSceneCandles(localLitMask);
                PlayCandleToggleFeedback(candleId);
            }
        }
    }

    public bool BlowOutNearestCandle(Vector3 pos, float radius)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)
        {
            return false;
        }

        CemeteryCandle best = null;
        float bestDist = radius;
        for (int i = 0; i < CemeteryCandle.AllCandles.Count; i++)
        {
            CemeteryCandle c = CemeteryCandle.AllCandles[i];
            if (c == null || !c.isActiveAndEnabled || !c.IsLit) continue;
            if (c.CandleId < 0 || c.CandleId >= 64) continue;

            float d = Vector2.Distance(pos, c.transform.position);
            if (d <= bestDist)
            {
                bestDist = d;
                best = c;
            }
        }

        if (best == null) return false;

        SetCandleLit(best.CandleId, false);
        if (ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(best.transform.position, 2, 8f);
        }
        return true;
    }

    private void ApplyMaskToSceneCandles(ulong mask)
    {
        for (int i = 0; i < CemeteryCandle.AllCandles.Count; i++)
        {
            CemeteryCandle c = CemeteryCandle.AllCandles[i];
            if (c == null) continue;
            int id = c.CandleId;
            if (id < 0 || id >= 64) continue;
            bool shouldBeLit = (mask & (1UL << id)) != 0UL;
            if (c.IsLit != shouldBeLit)
            {
                c.SetLitState(shouldBeLit);
            }
        }
    }

    private void PlayCandleToggleFeedback(int candleId)
    {
        Vector3 pos = transform.position;
        for (int i = 0; i < CemeteryCandle.AllCandles.Count; i++)
        {
            CemeteryCandle c = CemeteryCandle.AllCandles[i];
            if (c != null && c.CandleId == candleId)
            {
                pos = c.transform.position;
                break;
            }
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned && IsServer)
        {
            PlayCandleToggleClientRpc(pos);
        }
        else
        {
            GameAudioManager.PlayFlashlightClick(pos);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayCandleToggleClientRpc(Vector3 pos)
    {
        GameAudioManager.PlayFlashlightClick(pos);
    }
}
