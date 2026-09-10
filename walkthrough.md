# Complete Ghost AI Setup Guide

Since you haven't set anything up in your scene yet, follow this step-by-step tutorial to assemble your Ghost prefabs, the Handler, and your pathfinding nodes directly in the Unity Editor.

---

## Step 1: Create the Ghost Nodes (Pathfinding)

The Hunter Ghost uses these nodes to navigate the map. You need to create a network of them so the ghost knows where it can walk.

1. **Create a Node**:
   - Right-click in your Hierarchy and select **Create Empty**.
   - Name it `GhostNode`.
   - In the Inspector, click **Add Component** and add the [GhostNode.cs](file:///c:/Users/Zach%20Plata/Desktop/github/Pamahiin/Assets/Scripts/GhostNode.cs) script.
2. **Duplicate and Place**:
   - Duplicate this object (Ctrl+D / Cmd+D) multiple times.
   - Drag them around your map (e.g., in hallways, corners of rooms). 
   - *Tip: Keep them slightly elevated off the floor or away from walls to prevent the ghost from getting stuck.*
3. **Link the Nodes**:
   - Select one of your `GhostNode` objects.
   - In the Inspector, look at the **Neighbors** list in the script.
   - Click the `+` button and drag adjacent `GhostNode` objects from the Hierarchy into these slots.
   - *Because of the custom Gizmos added to the script, you will see yellow lines connecting the nodes in your Scene View! Make sure every node is connected to at least one other node.*
4. **Organize**:
   - Create a new Empty GameObject in the Hierarchy named `--- Nodes ---`.
   - Drag all your `GhostNode` objects inside it just to keep your Hierarchy clean.

---

## Step 2: Create the Ghost Room Markers

The ghosts need to know where their specific "Ghost Room" is so they know where to wander or return to.

1. Right-click in the Hierarchy and select **Create Empty**. Name it `GhostRoomMarker`.
2. Add a **BoxCollider2D** component to it. Check the **Is Trigger** box so players don't bump into it.
3. Add your existing **`GhostRoomMarker.cs`** script to it.
4. Scale and position the green box in the Scene View so it perfectly encapsulates the entire room you want the ghost to haunt.
5. **Duplicate this object** and place it in other rooms! The new script logic will automatically find all of them and randomly pick one at the start of every match.

---

## Step 3: Build the Ghost Entities

We need to create the parent Handler and the two distinct physical ghosts.

### A. The Parent Handler
1. Right-click in the Hierarchy and select **Create Empty**. Name it `Ghost_Parent`.
2. Add a **NetworkObject** component (required since this syncs over the network).
3. Add the [GhostHandler.cs](file:///c:/Users/Zach%20Plata/Desktop/github/Pamahiin/Assets/Scripts/GhostHandler.cs) script.
4. In the `GhostHandler` component, drag your `GhostRoomMarker` (from Step 2) into the **Ghostroom Marker** slot.
5. Make sure the **Obstacle Layer** is set to whatever layer you use for your walls (so the ghost can't see through walls).

### B. The Event Ghost (Child 1)
1. Right-click on `Ghost_Parent` and select **Create Empty** so it becomes a child object. Name it `EventGhost`.
2. Add a **SpriteRenderer** component and assign your ghost's sprite.
3. Add a **Rigidbody2D** component.
   - Set **Body Type** to `Kinematic` (or `Dynamic` with 0 gravity, depending on how your game physics work).
   - Freeze the Z Rotation in the constraints if necessary.
4. Add the [EventGhostBehavior.cs](file:///c:/Users/Zach%20Plata/Desktop/github/Pamahiin/Assets/Scripts/EventGhostBehavior.cs) script.
5. Add a **NetworkObject** component.
6. Look at the `EventGhostBehavior` component. Drag the `Ghost_Parent` object from the Hierarchy into the **Ghost Handler** dependency slot.

### C. The Hunter Ghost (Child 2)
1. Right-click on `Ghost_Parent` again and select **Create Empty** so it becomes a second child object. Name it `HunterGhost`.
2. Add a **SpriteRenderer** component and assign a scary ghost sprite!
3. Add a **Rigidbody2D** component (same settings as the Event Ghost).
4. Add a **Collider2D** (Box or Capsule) if you want it to physically bump into players during a hunt. Check `Is Trigger` if you handle the kill logic via triggers instead of physical collisions.
5. Add the [HunterGhostBehavior.cs](file:///c:/Users/Zach%20Plata/Desktop/github/Pamahiin/Assets/Scripts/HunterGhostBehavior.cs) script.
6. Add a **NetworkObject** component.
7. Look at the `HunterGhostBehavior` component. Drag the `Ghost_Parent` object from the Hierarchy into the **Ghost Handler** dependency slot.
8. Set the **Obstacle Layer** here as well (so the Hunter Ghost's line of sight gets blocked by walls).

---

## Step 4: Link the Children to the Parent

Now that the children are built, we need to tell the parent who they are.

1. Select your `Ghost_Parent` object.
2. In the `GhostHandler` script, you'll see slots for **Event Ghost** and **Hunter Ghost**.
3. Drag the `EventGhost` child object into the **Event Ghost** slot.
4. Drag the `HunterGhost` child object into the **Hunter Ghost** slot.

> [!TIP]
> You can now drag the `Ghost_Parent` from your Hierarchy down into your Project window to turn it into a reusable **Prefab**! This way you can easily spawn it via network code when a match starts.

---

## Summary of the Final Hierarchy

Your setup should look exactly like this in the Unity Editor:

```text
Hierarchy
│
├── GhostRoomMarker (Has BoxCollider2D trigger)
│
├── --- Nodes ---
│   ├── GhostNode (Has GhostNode.cs)
│   ├── GhostNode
│   └── GhostNode (etc...)
│
└── Ghost_Parent (Has GhostHandler.cs & NetworkObject)
    │
    ├── EventGhost (Has EventGhostBehavior.cs, SpriteRenderer, Rigidbody2D, NetworkObject)
    │
    └── HunterGhost (Has HunterGhostBehavior.cs, SpriteRenderer, Rigidbody2D, NetworkObject)
```

Once this is set up, you can hit Play. The Event Ghost will spawn and wander around the Ghost Room, while the Hunter Ghost will turn invisible and secretly patrol the node network!
