using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared canonical 3-floor geometry tables for San Nicolas High School (SchoolMap).
/// Used identically by both SchoolMapSceneBuilder (3D/2D Unity scene geometry, navmesh, doors, stairs)
/// and SchoolBlueprintGenerator (Journal brass-and-mahogany schematics) so schematics match the playable scene 1:1.
/// </summary>
public static class SchoolMapLayout
{
    public const float FLOOR_1_Y = 0f;
    public const float FLOOR_2_Y = -36f;
    public const float FLOOR_3_Y = -72f;

    public const float BUILDING_X_MIN = -6.50f;
    public const float BUILDING_X_MAX = 6.50f;
    public const float WEST_DIVIDER_X = -4.50f;
    public const float BRIDGE_X_MIN = -3.50f;
    public const float BRIDGE_X_MAX = 3.50f;
    public const float EAST_DIVIDER_X = 4.50f;

    public const float WING_Y_MIN = -4.25f;
    public const float WING_Y_MAX = 4.25f;
    public const float BRIDGE_Y_MIN = -2.25f;
    public const float BRIDGE_Y_MAX = 2.25f;
    public const float SPINE_Y_MIN = -0.45f;
    public const float SPINE_Y_MAX = 0.45f;

    public const float EXT_WALL_THICK = 0.125f;
    public const float INT_WALL_THICK = 0.09375f;

    public static float GetFloorBaseY(int floorIndex) => floorIndex switch
    {
        2 => FLOOR_2_Y,
        3 => FLOOR_3_Y,
        _ => FLOOR_1_Y
    };

    public enum FloorStyle
    {
        HallCheckerF1,
        HallConcrete,
        ClassroomGrid,
        RestroomMosaic,
        LibraryWood,
        WorkshopConcrete,
        ComLabRaised,
        CourtyardPaving,
        GrassLawn
    }

    public enum RoomRole
    {
        Corridor,
        Stairwell,
        Lobby,
        Classroom,
        Restroom,
        Workshop,
        Office,
        Library,
        Theater,
        ComLab,
        Closet,
        ControlSuite
    }

    public struct RoomDef
    {
        public int Floor;
        public string Id;
        public string DisplayName;
        public Rect LocalRect; // Relative to floor baseY
        public FloorStyle FloorStyle;
        public SurfaceType SurfaceType;
        public RoomRole Role;
        public bool HasGhostMarker;
        public bool HasLightSwitch;
        public bool IsHiddenCloset;
        public Vector2? SwitchLocalPos;

        public Rect WorldRect => ShiftRect(LocalRect, GetFloorBaseY(Floor));
    }

    public struct DoorDef
    {
        public int Floor;
        public string Name;
        public Vector2 LocalPos;
        public bool VerticalWall;
        public bool IsFrontDoor;
        public bool RequiresKey;
        public bool IsHidingSpot;
        public bool IsMetal;
        public bool IsLocker;
        public float SwingDeg;

        public Vector2 WorldPos => LocalPos + new Vector2(0f, GetFloorBaseY(Floor));
    }

    public struct StairCornerDef
    {
        public string CornerName; // NW, SW, NE, SE
        public bool IsWestWing;
        public Rect LocalAlcoveRect; // [xMin..xMax, yMin..yMax]
        public float LocalCenterY;
    }

    public struct HideZoneDef
    {
        public int Floor;
        public string Name;
        public Rect LocalRect;
        public Rect WorldRect => ShiftRect(LocalRect, GetFloorBaseY(Floor));
    }

    public struct GhostPropDef
    {
        public int Floor;
        public string Name;
        public string DisplayName;
        public string SheetResource;
        public Vector2 LocalPos;
        public bool Breakable;
        public string SfxName;
        public Vector2 WorldPos => LocalPos + new Vector2(0f, GetFloorBaseY(Floor));
    }

    public static readonly StairCornerDef[] StairCorners =
    {
        new StairCornerDef { CornerName = "NW", IsWestWing = true,  LocalAlcoveRect = new Rect(-6.50f,  1.65f, 2.00f, 1.20f), LocalCenterY =  2.25f },
        new StairCornerDef { CornerName = "SW", IsWestWing = true,  LocalAlcoveRect = new Rect(-6.50f, -2.85f, 2.00f, 1.20f), LocalCenterY = -2.25f },
        new StairCornerDef { CornerName = "NE", IsWestWing = false, LocalAlcoveRect = new Rect( 4.50f,  1.65f, 2.00f, 1.20f), LocalCenterY =  2.25f },
        new StairCornerDef { CornerName = "SE", IsWestWing = false, LocalAlcoveRect = new Rect( 4.50f, -2.85f, 2.00f, 1.20f), LocalCenterY = -2.25f }
    };

    /// <summary>H-wing collider rectangles (West Wing, Central Bridge, East Wing) shared by FloorZone and RoofFader per floor.</summary>
    public static Rect[] GetFloorWingBounds(int floorIndex)
    {
        float y = GetFloorBaseY(floorIndex);
        return new[]
        {
            ShiftRect(new Rect(-6.50f, -4.25f, 3.00f, 8.50f), y), // West Wing
            ShiftRect(new Rect(-3.50f, -2.25f, 7.00f, 4.50f), y), // Central Bridge
            ShiftRect(new Rect( 3.50f, -4.25f, 3.00f, 8.50f), y)  // East Wing
        };
    }

    public static List<RoomDef> BuildAllRooms()
    {
        var list = new List<RoomDef>();
        AddFloorCorridorsAndLobbies(list, 1);
        AddFloor1Rooms(list);

        AddFloorCorridorsAndLobbies(list, 2);
        AddFloor2Rooms(list);

        AddFloorCorridorsAndLobbies(list, 3);
        AddFloor3Rooms(list);
        return list;
    }

    private static void AddFloorCorridorsAndLobbies(List<RoomDef> list, int floor)
    {
        FloorStyle hall = floor == 1 ? FloorStyle.HallCheckerF1 : FloorStyle.HallConcrete;
        SurfaceType surf = floor == 1 ? SurfaceType.Pavement : SurfaceType.Concrete;

        // West Corridor & Lobby Tip
        list.Add(new RoomDef
        {
            Floor = floor,
            Id = $"F{floor}_Corridor_West",
            DisplayName = $"F{floor} West Corridor",
            LocalRect = new Rect(-4.50f, -4.25f, 1.00f, 8.50f),
            FloorStyle = hall,
            SurfaceType = surf,
            Role = RoomRole.Corridor,
            HasGhostMarker = false,
            HasLightSwitch = true,
            SwitchLocalPos = new Vector2(-4.40f, 0.62f)
        });

        // East Corridor & Lobby Tip
        list.Add(new RoomDef
        {
            Floor = floor,
            Id = $"F{floor}_Corridor_East",
            DisplayName = $"F{floor} East Corridor",
            LocalRect = new Rect(3.50f, -4.25f, 1.00f, 8.50f),
            FloorStyle = hall,
            SurfaceType = surf,
            Role = RoomRole.Corridor,
            HasGhostMarker = false,
            HasLightSwitch = true,
            SwitchLocalPos = new Vector2(4.40f, 0.62f)
        });

        // Central Spine Corridor
        list.Add(new RoomDef
        {
            Floor = floor,
            Id = $"F{floor}_Corridor_Central",
            DisplayName = $"F{floor} Central Hallway",
            LocalRect = new Rect(-3.50f, -0.45f, 7.00f, 0.90f),
            FloorStyle = hall,
            SurfaceType = surf,
            Role = RoomRole.Corridor,
            HasGhostMarker = false,
            HasLightSwitch = true,
            SwitchLocalPos = new Vector2(-0.10f, 0.34f)
        });

        // 4 Corner Stair Alcoves (no ghost spawn in stairwells)
        foreach (var sc in StairCorners)
        {
            list.Add(new RoomDef
            {
                Floor = floor,
                Id = $"F{floor}_Stairs_{sc.CornerName}",
                DisplayName = $"F{floor} {sc.CornerName} Stairs",
                LocalRect = sc.LocalAlcoveRect,
                FloorStyle = FloorStyle.HallConcrete,
                SurfaceType = SurfaceType.Concrete,
                Role = RoomRole.Stairwell,
                HasGhostMarker = false,
                HasLightSwitch = true,
                SwitchLocalPos = new Vector2(sc.IsWestWing ? -4.64f : 4.64f, sc.LocalCenterY + 0.44f)
            });
        }

        // 4 Corner Restrooms per floor
        AddRestroomsForFloor(list, floor);
    }

    private static void AddRestroomsForFloor(List<RoomDef> list, int floor)
    {
        (string corner, Rect r, Vector2 sw)[] rrs =
        {
            ("NW", new Rect(-6.50f,  2.85f, 2.00f, 1.40f), new Vector2(-4.62f,  3.15f)),
            ("SW", new Rect(-6.50f, -4.25f, 2.00f, 1.40f), new Vector2(-4.62f, -3.95f)),
            ("NE", new Rect( 4.50f,  2.85f, 2.00f, 1.40f), new Vector2( 4.62f,  3.15f)),
            ("SE", new Rect( 4.50f, -4.25f, 2.00f, 1.40f), new Vector2( 4.62f, -3.95f))
        };

        foreach (var item in rrs)
        {
            list.Add(new RoomDef
            {
                Floor = floor,
                Id = $"F{floor}_Restroom_{item.corner}",
                DisplayName = $"F{floor} {item.corner} Restroom",
                LocalRect = item.r,
                FloorStyle = FloorStyle.RestroomMosaic,
                SurfaceType = SurfaceType.Pavement,
                Role = RoomRole.Restroom,
                HasGhostMarker = true,
                HasLightSwitch = true,
                SwitchLocalPos = item.sw
            });
        }
    }

    private static void AddFloor1Rooms(List<RoomDef> list)
    {
        // West Wing classrooms
        list.Add(MakeRoom(1, "F1_Classroom_W1", "1F West Classroom 1", new Rect(-6.50f, 0.00f, 2.00f, 1.65f), FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(-4.62f, 0.42f)));
        list.Add(MakeRoom(1, "F1_Classroom_W2", "1F West Classroom 2", new Rect(-6.50f, -1.65f, 2.00f, 1.65f), FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(-4.62f, -1.22f)));

        // Central North Row: 5 partitioned classrooms + F1_Storage_NE
        string[] nIds = { "F1_Classroom_N1", "F1_Classroom_N2", "F1_Classroom_N3", "F1_Classroom_N4", "F1_Classroom_N5" };
        for (int i = 0; i < 5; i++)
        {
            float xMin = -3.50f + i * 1.25f;
            list.Add(MakeRoom(1, nIds[i], $"1F Room 10{i + 1}", new Rect(xMin, 0.45f, 1.25f, 1.80f),
                FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(xMin + 0.24f, 0.57f)));
        }
        list.Add(MakeRoom(1, "F1_Storage_NE", "1F North Supply Closet", new Rect(2.75f, 0.45f, 0.75f, 1.80f),
            FloorStyle.HallConcrete, SurfaceType.Concrete, RoomRole.Closet, new Vector2(2.89f, 0.57f), hiddenCloset: true));

        // Central South Row: Workshop, Classroom_S1, 3-part Dean's Office, Classroom_S2
        list.Add(MakeRoom(1, "F1_Workshop", "1F TLE Workshop", new Rect(-3.50f, -2.25f, 2.00f, 1.80f),
            FloorStyle.WorkshopConcrete, SurfaceType.Concrete, RoomRole.Workshop, new Vector2(-2.90f, -0.57f)));
        list.Add(MakeRoom(1, "F1_Classroom_S1", "1F Drafting Room", new Rect(-1.50f, -2.25f, 1.25f, 1.80f),
            FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(-1.26f, -0.57f)));
        list.Add(MakeRoom(1, "F1_DeansOffice_Inner", "Dean's Inner Office", new Rect(-0.25f, -2.25f, 0.75f, 1.80f),
            FloorStyle.LibraryWood, SurfaceType.Wood, RoomRole.Office, new Vector2(0.36f, -1.65f), hiddenCloset: true));
        list.Add(MakeRoom(1, "F1_DeansOffice_Reception", "Dean's Reception", new Rect(0.50f, -2.25f, 1.00f, 1.80f),
            FloorStyle.LibraryWood, SurfaceType.Wood, RoomRole.Office, new Vector2(0.68f, -0.57f)));
        list.Add(MakeRoom(1, "F1_DeansOffice_Annex", "Dean's Records Annex", new Rect(1.50f, -2.25f, 0.75f, 1.80f),
            FloorStyle.LibraryWood, SurfaceType.Wood, RoomRole.Office, new Vector2(1.62f, -1.65f), hiddenCloset: true));
        list.Add(MakeRoom(1, "F1_Classroom_S2", "1F South Lecture Room", new Rect(2.25f, -2.25f, 1.25f, 1.80f),
            FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(2.49f, -0.57f)));

        // East Wing: Locker Alcove (recessed bay along west edge of NE corridor south of Entry_NE_Courtyard) + Classroom_E1
        list.Add(MakeRoom(1, "F1_LockerAlcove", "1F Locker Alcove", new Rect(3.50f, 2.25f, 0.44f, 0.65f),
            FloorStyle.HallCheckerF1, SurfaceType.Pavement, RoomRole.Closet, new Vector2(3.62f, 2.75f), hasSwitch: false));
        list.Add(MakeRoom(1, "F1_Classroom_E1", "1F East Long Classroom", new Rect(4.50f, -1.65f, 2.00f, 3.30f),
            FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(4.62f, 0.55f)));
    }

    private static void AddFloor2Rooms(List<RoomDef> list)
    {
        // West Wing long classroom
        list.Add(MakeRoom(2, "F2_Classroom_W", "2F West Classroom", new Rect(-6.50f, -1.65f, 2.00f, 3.30f),
            FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(-4.62f, 0.55f)));

        // Central North Row: 4 partitioned classrooms
        for (int i = 0; i < 4; i++)
        {
            float xMin = -3.50f + i * 1.75f;
            list.Add(MakeRoom(2, $"F2_Classroom_N{i + 1}", $"2F Room 20{i + 1}", new Rect(xMin, 0.45f, 1.75f, 1.80f),
                FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(xMin + 0.30f, 0.57f)));
        }

        // Central South Row: Backstage, Multipurpose Hall/Theater, Library Discussion, Library
        list.Add(MakeRoom(2, "F2_Theater_Backstage", "Theater Backstage", new Rect(-3.50f, -2.25f, 0.75f, 1.80f),
            FloorStyle.HallConcrete, SurfaceType.Concrete, RoomRole.Closet, new Vector2(-2.88f, -1.70f), hiddenCloset: true));
        list.Add(MakeRoom(2, "F2_MultipurposeHall", "Multipurpose Theater Hall", new Rect(-2.75f, -2.25f, 2.50f, 1.80f),
            FloorStyle.LibraryWood, SurfaceType.Wood, RoomRole.Theater, new Vector2(-2.22f, -0.57f)));
        list.Add(MakeRoom(2, "F2_LibraryDiscussion", "Library Discussion Room", new Rect(-0.25f, -2.25f, 1.00f, 1.80f),
            FloorStyle.LibraryWood, SurfaceType.Wood, RoomRole.Library, new Vector2(-0.10f, -0.57f)));
        list.Add(MakeRoom(2, "F2_Library", "San Nicolas Library", new Rect(0.75f, -2.25f, 2.75f, 1.80f),
            FloorStyle.LibraryWood, SurfaceType.Wood, RoomRole.Library, new Vector2(1.05f, -0.57f)));

        // East Wing: 2 partitioned classrooms
        list.Add(MakeRoom(2, "F2_Classroom_E1", "2F East Classroom 1", new Rect(4.50f, 0.00f, 2.00f, 1.65f),
            FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(4.62f, 0.42f)));
        list.Add(MakeRoom(2, "F2_Classroom_E2", "2F East Classroom 2", new Rect(4.50f, -1.65f, 2.00f, 1.65f),
            FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(4.62f, -1.22f)));
    }

    private static void AddFloor3Rooms(List<RoomDef> list)
    {
        // West Wing: Janitor Closet south of NW Stairs + Long Classroom W
        list.Add(MakeRoom(3, "F3_JanitorCloset_NW", "3F NW Utility Closet", new Rect(-6.50f, 0.80f, 2.00f, 0.85f),
            FloorStyle.HallConcrete, SurfaceType.Concrete, RoomRole.Closet, new Vector2(-4.62f, 1.48f), hiddenCloset: true));
        list.Add(MakeRoom(3, "F3_Classroom_W", "3F West Classroom", new Rect(-6.50f, -1.65f, 2.00f, 2.45f),
            FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(-4.62f, 0.25f)));

        // Central North Row: ComLab 1, 2, 3
        list.Add(MakeRoom(3, "F3_ComLab_1", "Computer Lab 1", new Rect(-3.50f, 0.45f, 2.00f, 1.80f),
            FloorStyle.ComLabRaised, SurfaceType.Concrete, RoomRole.ComLab, new Vector2(-3.00f, 0.57f)));
        list.Add(MakeRoom(3, "F3_ComLab_2", "Computer Lab 2", new Rect(-1.50f, 0.45f, 2.50f, 1.80f),
            FloorStyle.ComLabRaised, SurfaceType.Concrete, RoomRole.ComLab, new Vector2(-1.00f, 0.57f)));
        list.Add(MakeRoom(3, "F3_ComLab_3", "Computer Lab 3", new Rect(1.00f, 0.45f, 2.50f, 1.80f),
            FloorStyle.ComLabRaised, SurfaceType.Concrete, RoomRole.ComLab, new Vector2(1.48f, 0.57f)));

        // Central South Row: 2 stacked utility closets on SW, 4 south classrooms (S4 w/ U-counter), Control Suite SE
        list.Add(MakeRoom(3, "F3_Utility_SW_Upper", "3F Upper Electrical Closet", new Rect(-3.50f, -1.35f, 0.875f, 0.90f),
            FloorStyle.WorkshopConcrete, SurfaceType.Concrete, RoomRole.Closet, new Vector2(-3.36f, -0.57f), hiddenCloset: true));
        list.Add(MakeRoom(3, "F3_Utility_SW_Lower", "3F Lower Archive Closet", new Rect(-3.50f, -2.25f, 0.875f, 0.90f),
            FloorStyle.WorkshopConcrete, SurfaceType.Concrete, RoomRole.Closet, new Vector2(-2.76f, -1.75f), hiddenCloset: true));

        for (int i = 0; i < 4; i++)
        {
            float xMin = -2.625f + i * 1.25f;
            list.Add(MakeRoom(3, $"F3_Classroom_S{i + 1}", $"3F Lab 30{i + 1}", new Rect(xMin, -2.25f, 1.25f, 1.80f),
                FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(xMin + 0.24f, -0.57f)));
        }
        list.Add(MakeRoom(3, "F3_ControlSuite_SE", "3F AV & Server Control Suite", new Rect(2.375f, -2.25f, 1.125f, 1.80f),
            FloorStyle.ComLabRaised, SurfaceType.Concrete, RoomRole.ControlSuite, new Vector2(2.55f, -0.57f), hiddenCloset: true));

        // East Wing: Long Classroom E
        list.Add(MakeRoom(3, "F3_Classroom_E", "3F East Classroom", new Rect(4.50f, -1.65f, 2.00f, 3.30f),
            FloorStyle.ClassroomGrid, SurfaceType.Pavement, RoomRole.Classroom, new Vector2(4.62f, 0.55f)));
    }

    private static RoomDef MakeRoom(
        int floor, string id, string display, Rect r, FloorStyle fs, SurfaceType st, RoomRole role,
        Vector2 sw, bool hasGhost = true, bool hasSwitch = true, bool hiddenCloset = false)
    {
        return new RoomDef
        {
            Floor = floor,
            Id = id,
            DisplayName = display,
            LocalRect = r,
            FloorStyle = fs,
            SurfaceType = st,
            Role = role,
            HasGhostMarker = hasGhost,
            HasLightSwitch = hasSwitch,
            IsHiddenCloset = hiddenCloset,
            SwitchLocalPos = sw
        };
    }

    public static List<DoorDef> BuildAllDoors()
    {
        var doors = new List<DoorDef>();

        // 4 Ground Exterior Entry Doors (all IsFrontDoor = true, RequiresKey = true, Metal Exit Doors)
        doors.Add(MakeDoor(1, "Entry_NW_Outer", new Vector2(-4.00f, WING_Y_MAX), vertical: false, front: true, metal: true, swingDeg: -90f));
        doors.Add(MakeDoor(1, "Entry_NW_Courtyard", new Vector2(BRIDGE_X_MIN, 3.45f), vertical: true, front: true, metal: true, swingDeg: 90f));
        doors.Add(MakeDoor(1, "Entry_NE_Outer", new Vector2(4.00f, WING_Y_MAX), vertical: false, front: true, metal: true, swingDeg: -90f));
        doors.Add(MakeDoor(1, "Entry_NE_Courtyard", new Vector2(BRIDGE_X_MAX, 3.45f), vertical: true, front: true, metal: true, swingDeg: -90f));

        // Restroom Doors + Stall Hide Doors on every floor
        for (int f = 1; f <= 3; f++)
        {
            doors.Add(MakeDoor(f, $"Door_F{f}_Restroom_NW", new Vector2(WEST_DIVIDER_X, 3.55f), vertical: true, swingDeg: 90f));
            doors.Add(MakeDoor(f, $"Door_F{f}_Restroom_SW", new Vector2(WEST_DIVIDER_X, -3.55f), vertical: true, swingDeg: -90f));
            doors.Add(MakeDoor(f, $"Door_F{f}_Restroom_NE", new Vector2(EAST_DIVIDER_X, 3.55f), vertical: true, swingDeg: -90f));
            doors.Add(MakeDoor(f, $"Door_F{f}_Restroom_SE", new Vector2(EAST_DIVIDER_X, -3.55f), vertical: true, swingDeg: 90f));

            // 8 Toilet Stall Doors per floor (2 per restroom)
            AddStallDoors(doors, f, "NW", -5.95f, -5.45f, 3.80f);
            AddStallDoors(doors, f, "SW", -5.95f, -5.45f, -3.80f);
            AddStallDoors(doors, f, "NE", 5.45f, 5.95f, 3.80f);
            AddStallDoors(doors, f, "SE", 5.45f, 5.95f, -3.80f);
        }

        // Floor 1 Interior Room Doors
        doors.Add(MakeDoor(1, "Door_F1_Classroom_W1", new Vector2(WEST_DIVIDER_X, 0.85f), vertical: true, swingDeg: 90f));
        doors.Add(MakeDoor(1, "Door_F1_Classroom_W2", new Vector2(WEST_DIVIDER_X, -0.85f), vertical: true, swingDeg: -90f));
        for (int i = 0; i < 5; i++)
        {
            doors.Add(MakeDoor(1, $"Door_F1_Classroom_N{i + 1}", new Vector2(-3.50f + i * 1.25f + 0.625f, SPINE_Y_MAX), vertical: false, swingDeg: 90f));
        }
        doors.Add(MakeDoor(1, "Door_F1_Storage_NE", new Vector2(3.125f, SPINE_Y_MAX), vertical: false, swingDeg: 90f));
        doors.Add(MakeDoor(1, "Door_F1_Workshop", new Vector2(-2.50f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(1, "Door_F1_Classroom_S1", new Vector2(-0.875f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(1, "Door_F1_DeansReception", new Vector2(1.00f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(1, "Door_F1_DeansInnerLink", new Vector2(0.50f, -1.25f), vertical: true, swingDeg: 90f));
        doors.Add(MakeDoor(1, "Door_F1_DeansAnnexLink", new Vector2(1.50f, -1.25f), vertical: true, swingDeg: -90f));
        doors.Add(MakeDoor(1, "Door_F1_Classroom_S2", new Vector2(2.875f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(1, "Door_F1_Classroom_E1_Top", new Vector2(EAST_DIVIDER_X, 0.90f), vertical: true, swingDeg: -90f));
        doors.Add(MakeDoor(1, "Door_F1_Classroom_E1_Bot", new Vector2(EAST_DIVIDER_X, -0.90f), vertical: true, swingDeg: -90f));
        doors.Add(MakeDoor(1, "Locker_F1_HideA", new Vector2(3.94f, 2.70f), vertical: true, hide: true, locker: true));
        doors.Add(MakeDoor(1, "Locker_F1_HideB", new Vector2(3.94f, 2.38f), vertical: true, hide: true, locker: true));

        // Floor 2 Interior Room Doors
        doors.Add(MakeDoor(2, "Door_F2_Classroom_W_Top", new Vector2(WEST_DIVIDER_X, 0.90f), vertical: true, swingDeg: 90f));
        doors.Add(MakeDoor(2, "Door_F2_Classroom_W_Bot", new Vector2(WEST_DIVIDER_X, -0.90f), vertical: true, swingDeg: 90f));
        for (int i = 0; i < 4; i++)
        {
            doors.Add(MakeDoor(2, $"Door_F2_Classroom_N{i + 1}", new Vector2(-3.50f + i * 1.75f + 0.875f, SPINE_Y_MAX), vertical: false, swingDeg: 90f));
        }
        doors.Add(MakeDoor(2, "Door_F2_Backstage", new Vector2(-2.75f, -1.35f), vertical: true, swingDeg: 90f));
        doors.Add(MakeDoor(2, "Door_F2_Multipurpose_W", new Vector2(-1.85f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(2, "Door_F2_Multipurpose_E", new Vector2(-0.75f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(2, "Door_F2_LibraryDiscussion", new Vector2(0.25f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(2, "Door_F2_Library_W", new Vector2(1.45f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(2, "Door_F2_Library_E", new Vector2(2.85f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(2, "Door_F2_Classroom_E1", new Vector2(EAST_DIVIDER_X, 0.85f), vertical: true, swingDeg: -90f));
        doors.Add(MakeDoor(2, "Door_F2_Classroom_E2", new Vector2(EAST_DIVIDER_X, -0.85f), vertical: true, swingDeg: -90f));

        // Floor 3 Interior Room Doors
        doors.Add(MakeDoor(3, "Door_F3_JanitorCloset_NW", new Vector2(WEST_DIVIDER_X, 1.22f), vertical: true, swingDeg: 90f));
        doors.Add(MakeDoor(3, "Door_F3_Classroom_W", new Vector2(WEST_DIVIDER_X, -0.50f), vertical: true, swingDeg: 90f));
        doors.Add(MakeDoor(3, "Door_F3_ComLab_1", new Vector2(-2.50f, SPINE_Y_MAX), vertical: false, swingDeg: 90f));
        doors.Add(MakeDoor(3, "Door_F3_ComLab_2", new Vector2(-0.25f, SPINE_Y_MAX), vertical: false, swingDeg: 90f));
        doors.Add(MakeDoor(3, "Door_F3_ComLab_3", new Vector2(2.25f, SPINE_Y_MAX), vertical: false, swingDeg: 90f));
        doors.Add(MakeDoor(3, "Door_F3_UtilityUpper", new Vector2(-3.0625f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(3, "Door_F3_UtilityLink", new Vector2(-3.0625f, -1.35f), vertical: false, swingDeg: -90f));
        for (int i = 0; i < 4; i++)
        {
            doors.Add(MakeDoor(3, $"Door_F3_Classroom_S{i + 1}", new Vector2(-2.625f + i * 1.25f + 0.625f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        }
        doors.Add(MakeDoor(3, "Door_F3_ControlSuite", new Vector2(2.9375f, SPINE_Y_MIN), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(3, "Door_F3_ControlSuite_Inner", new Vector2(2.9375f, -1.35f), vertical: false, swingDeg: -90f));
        doors.Add(MakeDoor(3, "Door_F3_Classroom_E_Top", new Vector2(EAST_DIVIDER_X, 0.90f), vertical: true, swingDeg: -90f));
        doors.Add(MakeDoor(3, "Door_F3_Classroom_E_Bot", new Vector2(EAST_DIVIDER_X, -0.90f), vertical: true, swingDeg: -90f));

        return doors;
    }

    private static void AddStallDoors(List<DoorDef> doors, int floor, string corner, float x1, float x2, float wallY)
    {
        doors.Add(MakeDoor(floor, $"Stall_F{floor}_{corner}_1", new Vector2(x1, wallY), vertical: false, hide: true));
        doors.Add(MakeDoor(floor, $"Stall_F{floor}_{corner}_2", new Vector2(x2, wallY), vertical: false, hide: true));
    }

    private static DoorDef MakeDoor(
        int floor, string name, Vector2 localPos, bool vertical, bool front = false,
        bool hide = false, bool metal = false, bool locker = false, float swingDeg = 90f)
    {
        return new DoorDef
        {
            Floor = floor,
            Name = name,
            LocalPos = localPos,
            VerticalWall = vertical,
            IsFrontDoor = front,
            RequiresKey = front,
            IsHidingSpot = hide,
            IsMetal = metal,
            IsLocker = locker,
            SwingDeg = swingDeg
        };
    }

    public static List<HideZoneDef> BuildAllHideZones()
    {
        var list = new List<HideZoneDef>();
        for (int f = 1; f <= 3; f++)
        {
            list.Add(MakeHide(f, $"Hide_F{f}_RestroomNW_Stall1", new Rect(-6.12f, 3.84f, 0.34f, 0.34f)));
            list.Add(MakeHide(f, $"Hide_F{f}_RestroomNW_Stall2", new Rect(-5.62f, 3.84f, 0.34f, 0.34f)));
            list.Add(MakeHide(f, $"Hide_F{f}_RestroomSW_Stall1", new Rect(-6.12f, -4.18f, 0.34f, 0.34f)));
            list.Add(MakeHide(f, $"Hide_F{f}_RestroomSW_Stall2", new Rect(-5.62f, -4.18f, 0.34f, 0.34f)));
            list.Add(MakeHide(f, $"Hide_F{f}_RestroomNE_Stall1", new Rect(5.28f, 3.84f, 0.34f, 0.34f)));
            list.Add(MakeHide(f, $"Hide_F{f}_RestroomNE_Stall2", new Rect(5.78f, 3.84f, 0.34f, 0.34f)));
            list.Add(MakeHide(f, $"Hide_F{f}_RestroomSE_Stall1", new Rect(5.28f, -4.18f, 0.34f, 0.34f)));
            list.Add(MakeHide(f, $"Hide_F{f}_RestroomSE_Stall2", new Rect(5.78f, -4.18f, 0.34f, 0.34f)));
        }

        // Floor 1 closets, lockers, and Dean's inner desk hideaway
        list.Add(MakeHide(1, "Hide_F1_StorageCloset", new Rect(2.85f, 1.82f, 0.55f, 0.35f)));
        list.Add(MakeHide(1, "Hide_F1_DeansInnerCorner", new Rect(-0.18f, -2.14f, 0.45f, 0.42f)));
        list.Add(MakeHide(1, "Hide_F1_LockerA", new Rect(3.62f, 2.59f, 0.30f, 0.25f)));
        list.Add(MakeHide(1, "Hide_F1_LockerB", new Rect(3.62f, 2.30f, 0.30f, 0.21f)));

        // Floor 2 backstage & stage edge
        list.Add(MakeHide(2, "Hide_F2_TheaterBackstage", new Rect(-3.40f, -2.15f, 0.52f, 0.45f)));
        list.Add(MakeHide(2, "Hide_F2_StageWing", new Rect(-2.65f, -2.12f, 0.38f, 0.40f)));
        list.Add(MakeHide(2, "Hide_F2_LibraryStacks", new Rect(3.02f, -2.14f, 0.40f, 0.36f)));

        // Floor 3 janitor closet, utility closets, control suite
        list.Add(MakeHide(3, "Hide_F3_JanitorCloset", new Rect(-6.40f, 0.90f, 0.50f, 0.55f)));
        list.Add(MakeHide(3, "Hide_F3_UtilityLower", new Rect(-3.40f, -2.15f, 0.55f, 0.48f)));
        list.Add(MakeHide(3, "Hide_F3_ControlBooth", new Rect(3.04f, -2.14f, 0.40f, 0.44f)));
        return list;
    }

    private static HideZoneDef MakeHide(int f, string name, Rect r) => new HideZoneDef { Floor = f, Name = name, LocalRect = r };

    public static List<GhostPropDef> BuildAllGhostProps()
    {
        return new List<GhostPropDef>
        {
            // Floor 1
            MakeProp(1, "Prop_F1_N1_Chair", "Student Chair", "SchoolMap/Props/prop_chair_sheet", new Vector2(-2.90f, 1.32f), false, "prop_knock_wood"),
            MakeProp(1, "Prop_F1_N3_Mug", "Teacher Mug", "SchoolMap/Props/prop_mug_sheet", new Vector2(-0.35f, 1.85f), true, "prop_shatter_glass"),
            MakeProp(1, "Prop_F1_Workshop_Tools", "Workshop Wrenches", "SchoolMap/Props/prop_tools_sheet", new Vector2(-2.70f, -1.55f), false, "prop_knock_wood"),
            MakeProp(1, "Prop_F1_Deans_Papers", "Student Files", "SchoolMap/Props/prop_papers_sheet", new Vector2(1.00f, -1.65f), false, "prop_knock_wood"),
            MakeProp(1, "Prop_F1_E1_Books", "Textbooks", "SchoolMap/Props/prop_books_sheet", new Vector2(5.55f, 0.25f), false, "prop_knock_wood"),

            // Floor 2
            MakeProp(2, "Prop_F2_W_Papers", "Exam Stack", "SchoolMap/Props/prop_papers_sheet", new Vector2(-5.45f, 0.45f), false, "prop_knock_wood"),
            MakeProp(2, "Prop_F2_Theater_Chair", "Auditorium Seat", "SchoolMap/Props/prop_chair_sheet", new Vector2(-1.30f, -1.35f), false, "prop_knock_wood"),
            MakeProp(2, "Prop_F2_Discussion_Mug", "Ceramic Cup", "SchoolMap/Props/prop_mug_sheet", new Vector2(0.25f, -1.35f), true, "prop_shatter_glass"),
            MakeProp(2, "Prop_F2_Library_Books1", "Folio Stack", "SchoolMap/Props/prop_books_sheet", new Vector2(2.05f, -1.42f), false, "prop_knock_wood"),
            MakeProp(2, "Prop_F2_Library_Papers", "Archive Ledger", "SchoolMap/Props/prop_papers_sheet", new Vector2(1.30f, -0.95f), false, "prop_knock_wood"),

            // Floor 3
            MakeProp(3, "Prop_F3_ComLab1_KB", "CRT Keyboard", "SchoolMap/Props/prop_keyboard_sheet", new Vector2(-2.50f, 1.50f), false, "prop_knock_wood"),
            MakeProp(3, "Prop_F3_ComLab2_KB", "CRT Keyboard", "SchoolMap/Props/prop_keyboard_sheet", new Vector2(-0.25f, 1.50f), false, "prop_knock_wood"),
            MakeProp(3, "Prop_F3_ComLab3_Mug", "Technician Mug", "SchoolMap/Props/prop_mug_sheet", new Vector2(2.25f, 1.85f), true, "prop_shatter_glass"),
            MakeProp(3, "Prop_F3_Control_Tools", "Electrical Tools", "SchoolMap/Props/prop_tools_sheet", new Vector2(2.90f, -1.80f), false, "prop_knock_wood"),
            MakeProp(3, "Prop_F3_S2_Books", "Science Manual", "SchoolMap/Props/prop_books_sheet", new Vector2(-0.75f, -1.35f), false, "prop_knock_wood")
        };
    }

    private static GhostPropDef MakeProp(int f, string name, string display, string sheet, Vector2 pos, bool breakable, string sfx) =>
        new GhostPropDef
        {
            Floor = f,
            Name = name,
            DisplayName = display,
            SheetResource = sheet,
            LocalPos = pos,
            Breakable = breakable,
            SfxName = sfx
        };

    public static Rect ShiftRect(Rect r, float deltaY)
    {
        return new Rect(r.xMin, r.yMin + deltaY, r.width, r.height);
    }
}
