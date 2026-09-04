using SMGEditor.Core.Formats;

namespace SMGEditor.Core.Stage;

public static class PlacementSchemas
{
    public static BCSVTable? ForGame(int game, string listName)
    {
        if (listName == "GeneralPosInfo")
        {
            return game == 1 ? GeneralPosInfoSmg1() : GeneralPosInfoSmg2();
        }

        return game == 2 ? ForSmg2(listName) : null;
    }

    public static BCSVTable? ForSmg2(string listName) => listName switch
    {
        "ObjInfo" => ObjInfo(),
        "MapPartsInfo" => MapPartsInfo(),
        "AreaObjInfo" => AreaObjInfo(),
        "CameraCubeInfo" => CameraCubeInfo(),
        "PlanetObjInfo" => PlanetObjInfo(),
        "DemoObjInfo" => DemoObjInfo(),
        "StartInfo" => StartInfo(),
        "StageObjInfo" => StageObjInfo(),
        "GeneralPosInfo" => GeneralPosInfoSmg2(),
        _ => null,
    };

    private static BCSVTable GeneralPosInfoSmg2() => new()
    {
        Rows = [],
        EntrySize = 0x24,
        DataOffset = 0x7C,
        Fields =
        [
            new("name", 0x00337A8Bu, 0xFFFFFFFFu, 0x1A, 0, BCSVValueType.StringOffset),
            new("PosName", 0x4BD5EEDFu, 0xFFFFFFFFu, 0x1E, 0, BCSVValueType.StringOffset),
            new("pos_x", 0x065E794Du, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Float),
            new("pos_y", 0x065E794Eu, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Float),
            new("pos_z", 0x065E794Fu, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Float),
            new("dir_x", 0x05B2A146u, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Float),
            new("dir_y", 0x05B2A147u, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Float),
            new("dir_z", 0x05B2A148u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Float),
            new("Obj_ID", 0x8C657583u, 0x0000FFFFu, 0x18, 0, BCSVValueType.Short),
        ],
    };

    private static BCSVTable GeneralPosInfoSmg1() => new()
    {
        Rows = [],
        EntrySize = 0x24,
        DataOffset = 0x88,
        Fields =
        [
            new("name", 0x00337A8Bu, 0xFFFFFFFFu, 0x1C, 0, BCSVValueType.StringOffset),
            new("PosName", 0x4BD5EEDFu, 0xFFFFFFFFu, 0x20, 0, BCSVValueType.StringOffset),
            new("pos_x", 0x065E794Du, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Float),
            new("pos_y", 0x065E794Eu, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Float),
            new("pos_z", 0x065E794Fu, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Float),
            new("dir_x", 0x05B2A146u, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Float),
            new("dir_y", 0x05B2A147u, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Float),
            new("dir_z", 0x05B2A148u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Float),
            new("Obj_ID", 0x8C657583u, 0x0000FFFFu, 0x18, 0, BCSVValueType.Short),
            new("ChildObjId", 0xC6AE1CD6u, 0x0000FFFFu, 0x1A, 0, BCSVValueType.Short),
        ],
    };

    private static BCSVTable ObjInfo() => new()
    {
        Rows = [],
        EntrySize = 0x88,
        DataOffset = 0x1D8,
        Fields =
        [
            new("name", 0x00337A8Bu, 0xFFFFFFFFu, 0x84, 0, BCSVValueType.StringOffset),
            new("l_id", 0x003289CEu, 0xFFFFFFFFu, 0x28, 0, BCSVValueType.Long),
            new("Obj_arg0", 0x08E9C302u, 0xFFFFFFFFu, 0x2C, 0, BCSVValueType.Long),
            new("Obj_arg1", 0x08E9C303u, 0xFFFFFFFFu, 0x30, 0, BCSVValueType.Long),
            new("Obj_arg2", 0x08E9C304u, 0xFFFFFFFFu, 0x34, 0, BCSVValueType.Long),
            new("Obj_arg3", 0x08E9C305u, 0xFFFFFFFFu, 0x38, 0, BCSVValueType.Long),
            new("Obj_arg4", 0x08E9C306u, 0xFFFFFFFFu, 0x3C, 0, BCSVValueType.Long),
            new("Obj_arg5", 0x08E9C307u, 0xFFFFFFFFu, 0x40, 0, BCSVValueType.Long),
            new("Obj_arg6", 0x08E9C308u, 0xFFFFFFFFu, 0x44, 0, BCSVValueType.Long),
            new("Obj_arg7", 0x08E9C309u, 0xFFFFFFFFu, 0x48, 0, BCSVValueType.Long),
            new("CameraSetId", 0xDD7658D8u, 0xFFFFFFFFu, 0x4C, 0, BCSVValueType.Long),
            new("SW_APPEAR", 0x749DFBD0u, 0xFFFFFFFFu, 0x50, 0, BCSVValueType.Long),
            new("SW_DEAD", 0xC075815Fu, 0xFFFFFFFFu, 0x54, 0, BCSVValueType.Long),
            new("SW_A", 0x00270D26u, 0xFFFFFFFFu, 0x58, 0, BCSVValueType.Long),
            new("SW_B", 0x00270D27u, 0xFFFFFFFFu, 0x5C, 0, BCSVValueType.Long),
            new("SW_AWAKE", 0x4E1893CAu, 0xFFFFFFFFu, 0x60, 0, BCSVValueType.Long),
            new("SW_PARAM", 0x4EE232D2u, 0xFFFFFFFFu, 0x64, 0, BCSVValueType.Long),
            new("MessageId", 0x219D4362u, 0xFFFFFFFFu, 0x68, 0, BCSVValueType.Long),
            new("ParamScale", 0x91242CDDu, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Float),
            new("pos_x", 0x065E794Du, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Float),
            new("pos_y", 0x065E794Eu, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Float),
            new("pos_z", 0x065E794Fu, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Float),
            new("dir_x", 0x05B2A146u, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Float),
            new("dir_y", 0x05B2A147u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Float),
            new("dir_z", 0x05B2A148u, 0xFFFFFFFFu, 0x18, 0, BCSVValueType.Float),
            new("scale_x", 0x71E5EAC3u, 0xFFFFFFFFu, 0x1C, 0, BCSVValueType.Float),
            new("scale_y", 0x71E5EAC4u, 0xFFFFFFFFu, 0x20, 0, BCSVValueType.Float),
            new("scale_z", 0x71E5EAC5u, 0xFFFFFFFFu, 0x24, 0, BCSVValueType.Float),
            new("CastId", 0x77E19CDAu, 0xFFFFFFFFu, 0x6C, 0, BCSVValueType.Long),
            new("ViewGroupId", 0x74550D75u, 0xFFFFFFFFu, 0x70, 0, BCSVValueType.Long),
            new("ShapeModelNo", 0x1176D409u, 0x0000FFFFu, 0x74, 0, BCSVValueType.Short),
            new("CommonPath_ID", 0x4B700AEAu, 0x0000FFFFu, 0x76, 0, BCSVValueType.Short),
            new("ClippingGroupId", 0x4B5830B8u, 0x0000FFFFu, 0x78, 0, BCSVValueType.Short),
            new("GroupId", 0x74B5F3DAu, 0x0000FFFFu, 0x7A, 0, BCSVValueType.Short),
            new("DemoGroupId", 0x8E34C877u, 0x0000FFFFu, 0x7C, 0, BCSVValueType.Short),
            new("MapParts_ID", 0x81497C36u, 0x0000FFFFu, 0x7E, 0, BCSVValueType.Short),
            new("Obj_ID", 0x8C657583u, 0x0000FFFFu, 0x80, 0, BCSVValueType.Short),
            new("GeneratorID", 0x9848E30Eu, 0x0000FFFFu, 0x82, 0, BCSVValueType.Short),
        ],
    };

    private static BCSVTable MapPartsInfo() => new()
    {
        Rows = [],
        EntrySize = 0xA0,
        DataOffset = 0x220,
        Fields =
        [
            new("name", 0x00337A8Bu, 0xFFFFFFFFu, 0x9C, 0, BCSVValueType.StringOffset),
            new("l_id", 0x003289CEu, 0xFFFFFFFFu, 0x28, 0, BCSVValueType.Long),
            new("MoveConditionType", 0x1A691A84u, 0xFFFFFFFFu, 0x2C, 0, BCSVValueType.Long),
            new("RotateSpeed", 0xD1DFFB8Cu, 0xFFFFFFFFu, 0x30, 0, BCSVValueType.Long),
            new("RotateAngle", 0xD0E17418u, 0xFFFFFFFFu, 0x34, 0, BCSVValueType.Long),
            new("RotateAxis", 0x7217EFBCu, 0xFFFFFFFFu, 0x38, 0, BCSVValueType.Long),
            new("RotateAccelType", 0x86AC8907u, 0xFFFFFFFFu, 0x3C, 0, BCSVValueType.Long),
            new("RotateStopTime", 0x4558808Au, 0xFFFFFFFFu, 0x40, 0, BCSVValueType.Long),
            new("RotateType", 0x72209755u, 0xFFFFFFFFu, 0x44, 0, BCSVValueType.Long),
            new("ShadowType", 0x39FCC89Au, 0xFFFFFFFFu, 0x48, 0, BCSVValueType.Long),
            new("SignMotionType", 0x6D3E35CDu, 0xFFFFFFFFu, 0x4C, 0, BCSVValueType.Long),
            new("PressType", 0x4137EDFDu, 0xFFFFFFFFu, 0x50, 0, BCSVValueType.Long),
            new("ParamScale", 0x91242CDDu, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Float),
            new("CameraSetId", 0xDD7658D8u, 0xFFFFFFFFu, 0x54, 0, BCSVValueType.Long),
            new("FarClip", 0x22E0D6E7u, 0xFFFFFFFFu, 0x58, 0, BCSVValueType.Long),
            new("Obj_arg0", 0x08E9C302u, 0xFFFFFFFFu, 0x5C, 0, BCSVValueType.Long),
            new("Obj_arg1", 0x08E9C303u, 0xFFFFFFFFu, 0x60, 0, BCSVValueType.Long),
            new("Obj_arg2", 0x08E9C304u, 0xFFFFFFFFu, 0x64, 0, BCSVValueType.Long),
            new("Obj_arg3", 0x08E9C305u, 0xFFFFFFFFu, 0x68, 0, BCSVValueType.Long),
            new("SW_APPEAR", 0x749DFBD0u, 0xFFFFFFFFu, 0x6C, 0, BCSVValueType.Long),
            new("SW_DEAD", 0xC075815Fu, 0xFFFFFFFFu, 0x70, 0, BCSVValueType.Long),
            new("SW_A", 0x00270D26u, 0xFFFFFFFFu, 0x74, 0, BCSVValueType.Long),
            new("SW_B", 0x00270D27u, 0xFFFFFFFFu, 0x78, 0, BCSVValueType.Long),
            new("SW_AWAKE", 0x4E1893CAu, 0xFFFFFFFFu, 0x7C, 0, BCSVValueType.Long),
            new("SW_PARAM", 0x4EE232D2u, 0xFFFFFFFFu, 0x80, 0, BCSVValueType.Long),
            new("pos_x", 0x065E794Du, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Float),
            new("pos_y", 0x065E794Eu, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Float),
            new("pos_z", 0x065E794Fu, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Float),
            new("dir_x", 0x05B2A146u, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Float),
            new("dir_y", 0x05B2A147u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Float),
            new("dir_z", 0x05B2A148u, 0xFFFFFFFFu, 0x18, 0, BCSVValueType.Float),
            new("scale_x", 0x71E5EAC3u, 0xFFFFFFFFu, 0x1C, 0, BCSVValueType.Float),
            new("scale_y", 0x71E5EAC4u, 0xFFFFFFFFu, 0x20, 0, BCSVValueType.Float),
            new("scale_z", 0x71E5EAC5u, 0xFFFFFFFFu, 0x24, 0, BCSVValueType.Float),
            new("CastId", 0x77E19CDAu, 0xFFFFFFFFu, 0x84, 0, BCSVValueType.Long),
            new("ViewGroupId", 0x74550D75u, 0xFFFFFFFFu, 0x88, 0, BCSVValueType.Long),
            new("ShapeModelNo", 0x1176D409u, 0x0000FFFFu, 0x8C, 0, BCSVValueType.Short),
            new("CommonPath_ID", 0x4B700AEAu, 0x0000FFFFu, 0x8E, 0, BCSVValueType.Short),
            new("ClippingGroupId", 0x4B5830B8u, 0x0000FFFFu, 0x90, 0, BCSVValueType.Short),
            new("GroupId", 0x74B5F3DAu, 0x0000FFFFu, 0x92, 0, BCSVValueType.Short),
            new("DemoGroupId", 0x8E34C877u, 0x0000FFFFu, 0x94, 0, BCSVValueType.Short),
            new("MapParts_ID", 0x81497C36u, 0x0000FFFFu, 0x96, 0, BCSVValueType.Short),
            new("Obj_ID", 0x8C657583u, 0x0000FFFFu, 0x98, 0, BCSVValueType.Short),
            new("ParentId", 0x49E5F385u, 0x0000FFFFu, 0x9A, 0, BCSVValueType.Short),
        ],
    };

    private static BCSVTable AreaObjInfo() => new()
    {
        Rows = [],
        EntrySize = 0x74,
        DataOffset = 0x190,
        Fields =
        [
            new("name", 0x00337A8Bu, 0xFFFFFFFFu, 0x6E, 0, BCSVValueType.StringOffset),
            new("l_id", 0x003289CEu, 0xFFFFFFFFu, 0x24, 0, BCSVValueType.Long),
            new("Obj_arg0", 0x08E9C302u, 0xFFFFFFFFu, 0x28, 0, BCSVValueType.Long),
            new("Obj_arg1", 0x08E9C303u, 0xFFFFFFFFu, 0x2C, 0, BCSVValueType.Long),
            new("Obj_arg2", 0x08E9C304u, 0xFFFFFFFFu, 0x30, 0, BCSVValueType.Long),
            new("Obj_arg3", 0x08E9C305u, 0xFFFFFFFFu, 0x34, 0, BCSVValueType.Long),
            new("Obj_arg4", 0x08E9C306u, 0xFFFFFFFFu, 0x38, 0, BCSVValueType.Long),
            new("Obj_arg5", 0x08E9C307u, 0xFFFFFFFFu, 0x3C, 0, BCSVValueType.Long),
            new("Obj_arg6", 0x08E9C308u, 0xFFFFFFFFu, 0x40, 0, BCSVValueType.Long),
            new("Obj_arg7", 0x08E9C309u, 0xFFFFFFFFu, 0x44, 0, BCSVValueType.Long),
            new("Priority", 0xBE62DDC4u, 0xFFFFFFFFu, 0x48, 0, BCSVValueType.Long),
            new("SW_APPEAR", 0x749DFBD0u, 0xFFFFFFFFu, 0x4C, 0, BCSVValueType.Long),
            new("SW_A", 0x00270D26u, 0xFFFFFFFFu, 0x50, 0, BCSVValueType.Long),
            new("SW_B", 0x00270D27u, 0xFFFFFFFFu, 0x54, 0, BCSVValueType.Long),
            new("SW_AWAKE", 0x4E1893CAu, 0xFFFFFFFFu, 0x58, 0, BCSVValueType.Long),
            new("pos_x", 0x065E794Du, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Float),
            new("pos_y", 0x065E794Eu, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Float),
            new("pos_z", 0x065E794Fu, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Float),
            new("dir_x", 0x05B2A146u, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Float),
            new("dir_y", 0x05B2A147u, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Float),
            new("dir_z", 0x05B2A148u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Float),
            new("scale_x", 0x71E5EAC3u, 0xFFFFFFFFu, 0x18, 0, BCSVValueType.Float),
            new("scale_y", 0x71E5EAC4u, 0xFFFFFFFFu, 0x1C, 0, BCSVValueType.Float),
            new("scale_z", 0x71E5EAC5u, 0xFFFFFFFFu, 0x20, 0, BCSVValueType.Float),
            new("FollowId", 0x15D780CCu, 0xFFFFFFFFu, 0x5C, 0, BCSVValueType.Long),
            new("AreaShapeNo", 0x4FC01BD5u, 0x0000FFFFu, 0x60, 0, BCSVValueType.Short),
            new("CommonPath_ID", 0x4B700AEAu, 0x0000FFFFu, 0x62, 0, BCSVValueType.Short),
            new("ClippingGroupId", 0x4B5830B8u, 0x0000FFFFu, 0x64, 0, BCSVValueType.Short),
            new("GroupId", 0x74B5F3DAu, 0x0000FFFFu, 0x66, 0, BCSVValueType.Short),
            new("DemoGroupId", 0x8E34C877u, 0x0000FFFFu, 0x68, 0, BCSVValueType.Short),
            new("MapParts_ID", 0x81497C36u, 0x0000FFFFu, 0x6A, 0, BCSVValueType.Short),
            new("Obj_ID", 0x8C657583u, 0x0000FFFFu, 0x6C, 0, BCSVValueType.Short),
        ],
    };

    private static BCSVTable CameraCubeInfo() => new()
    {
        Rows = [],
        EntrySize = 0x64,
        DataOffset = 0x148,
        Fields =
        [
            new("name", 0x00337A8Bu, 0xFFFFFFFFu, 0x5A, 0, BCSVValueType.StringOffset),
            new("l_id", 0x003289CEu, 0xFFFFFFFFu, 0x24, 0, BCSVValueType.Long),
            new("Obj_arg0", 0x08E9C302u, 0xFFFFFFFFu, 0x28, 0, BCSVValueType.Long),
            new("Obj_arg1", 0x08E9C303u, 0xFFFFFFFFu, 0x2C, 0, BCSVValueType.Long),
            new("Obj_arg2", 0x08E9C304u, 0xFFFFFFFFu, 0x30, 0, BCSVValueType.Long),
            new("Obj_arg3", 0x08E9C305u, 0xFFFFFFFFu, 0x34, 0, BCSVValueType.Long),
            new("InterpolateIn", 0x50F5D5E6u, 0xFFFFFFFFu, 0x38, 0, BCSVValueType.Long),
            new("InterpolateOut", 0xCDC4FEADu, 0xFFFFFFFFu, 0x3C, 0, BCSVValueType.Long),
            new("SW_APPEAR", 0x749DFBD0u, 0xFFFFFFFFu, 0x40, 0, BCSVValueType.Long),
            new("SW_A", 0x00270D26u, 0xFFFFFFFFu, 0x44, 0, BCSVValueType.Long),
            new("SW_B", 0x00270D27u, 0xFFFFFFFFu, 0x48, 0, BCSVValueType.Long),
            new("SW_AWAKE", 0x4E1893CAu, 0xFFFFFFFFu, 0x4C, 0, BCSVValueType.Long),
            new("Validity", 0xAF239B52u, 0xFFFFFFFFu, 0x60, 0, BCSVValueType.StringOffset),
            new("pos_x", 0x065E794Du, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Float),
            new("pos_y", 0x065E794Eu, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Float),
            new("pos_z", 0x065E794Fu, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Float),
            new("dir_x", 0x05B2A146u, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Float),
            new("dir_y", 0x05B2A147u, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Float),
            new("dir_z", 0x05B2A148u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Float),
            new("scale_x", 0x71E5EAC3u, 0xFFFFFFFFu, 0x18, 0, BCSVValueType.Float),
            new("scale_y", 0x71E5EAC4u, 0xFFFFFFFFu, 0x1C, 0, BCSVValueType.Float),
            new("scale_z", 0x71E5EAC5u, 0xFFFFFFFFu, 0x20, 0, BCSVValueType.Float),
            new("FollowId", 0x15D780CCu, 0xFFFFFFFFu, 0x50, 0, BCSVValueType.Long),
            new("AreaShapeNo", 0x4FC01BD5u, 0x0000FFFFu, 0x54, 0, BCSVValueType.Short),
            new("MapParts_ID", 0x81497C36u, 0x0000FFFFu, 0x56, 0, BCSVValueType.Short),
            new("Obj_ID", 0x8C657583u, 0x0000FFFFu, 0x58, 0, BCSVValueType.Short),
        ],
    };

    private static BCSVTable PlanetObjInfo() => new()
    {
        Rows = [],
        EntrySize = 0x78,
        DataOffset = 0x19C,
        Fields =
        [
            new("name", 0x00337A8Bu, 0xFFFFFFFFu, 0x6C, 0, BCSVValueType.StringOffset),
            new("l_id", 0x003289CEu, 0xFFFFFFFFu, 0x2C, 0, BCSVValueType.Long),
            new("Obj_arg0", 0x08E9C302u, 0xFFFFFFFFu, 0x30, 0, BCSVValueType.Long),
            new("Obj_arg1", 0x08E9C303u, 0xFFFFFFFFu, 0x34, 0, BCSVValueType.Long),
            new("Obj_arg2", 0x08E9C304u, 0xFFFFFFFFu, 0x38, 0, BCSVValueType.Long),
            new("Obj_arg3", 0x08E9C305u, 0xFFFFFFFFu, 0x3C, 0, BCSVValueType.Long),
            new("Range", 0x04B1491Du, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Float),
            new("Distant", 0xC6DF5A61u, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Float),
            new("Priority", 0xBE62DDC4u, 0xFFFFFFFFu, 0x40, 0, BCSVValueType.Long),
            new("Inverse", 0xD80A7310u, 0xFFFFFFFFu, 0x44, 0, BCSVValueType.Long),
            new("Power", 0x049B98E5u, 0xFFFFFFFFu, 0x70, 0, BCSVValueType.StringOffset),
            new("Gravity_type", 0xBF156AABu, 0xFFFFFFFFu, 0x74, 0, BCSVValueType.StringOffset),
            new("SW_APPEAR", 0x749DFBD0u, 0xFFFFFFFFu, 0x48, 0, BCSVValueType.Long),
            new("SW_DEAD", 0xC075815Fu, 0xFFFFFFFFu, 0x4C, 0, BCSVValueType.Long),
            new("SW_A", 0x00270D26u, 0xFFFFFFFFu, 0x50, 0, BCSVValueType.Long),
            new("SW_B", 0x00270D27u, 0xFFFFFFFFu, 0x54, 0, BCSVValueType.Long),
            new("SW_AWAKE", 0x4E1893CAu, 0xFFFFFFFFu, 0x58, 0, BCSVValueType.Long),
            new("pos_x", 0x065E794Du, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Float),
            new("pos_y", 0x065E794Eu, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Float),
            new("pos_z", 0x065E794Fu, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Float),
            new("dir_x", 0x05B2A146u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Float),
            new("dir_y", 0x05B2A147u, 0xFFFFFFFFu, 0x18, 0, BCSVValueType.Float),
            new("dir_z", 0x05B2A148u, 0xFFFFFFFFu, 0x1C, 0, BCSVValueType.Float),
            new("scale_x", 0x71E5EAC3u, 0xFFFFFFFFu, 0x20, 0, BCSVValueType.Float),
            new("scale_y", 0x71E5EAC4u, 0xFFFFFFFFu, 0x24, 0, BCSVValueType.Float),
            new("scale_z", 0x71E5EAC5u, 0xFFFFFFFFu, 0x28, 0, BCSVValueType.Float),
            new("FollowId", 0x15D780CCu, 0xFFFFFFFFu, 0x5C, 0, BCSVValueType.Long),
            new("CommonPath_ID", 0x4B700AEAu, 0x0000FFFFu, 0x60, 0, BCSVValueType.Short),
            new("ClippingGroupId", 0x4B5830B8u, 0x0000FFFFu, 0x62, 0, BCSVValueType.Short),
            new("GroupId", 0x74B5F3DAu, 0x0000FFFFu, 0x64, 0, BCSVValueType.Short),
            new("DemoGroupId", 0x8E34C877u, 0x0000FFFFu, 0x66, 0, BCSVValueType.Short),
            new("MapParts_ID", 0x81497C36u, 0x0000FFFFu, 0x68, 0, BCSVValueType.Short),
            new("Obj_ID", 0x8C657583u, 0x0000FFFFu, 0x6A, 0, BCSVValueType.Short),
        ],
    };

    private static BCSVTable DemoObjInfo() => new()
    {
        Rows = [],
        EntrySize = 0x48,
        DataOffset = 0xE8,
        Fields =
        [
            new("name", 0x00337A8Bu, 0xFFFFFFFFu, 0x3C, 0, BCSVValueType.StringOffset),
            new("DemoName", 0x36E6A72Eu, 0xFFFFFFFFu, 0x40, 0, BCSVValueType.StringOffset),
            new("TimeSheetName", 0x0895581Du, 0xFFFFFFFFu, 0x44, 0, BCSVValueType.StringOffset),
            new("l_id", 0x003289CEu, 0xFFFFFFFFu, 0x24, 0, BCSVValueType.Long),
            new("SW_APPEAR", 0x749DFBD0u, 0xFFFFFFFFu, 0x28, 0, BCSVValueType.Long),
            new("SW_DEAD", 0xC075815Fu, 0xFFFFFFFFu, 0x2C, 0, BCSVValueType.Long),
            new("SW_A", 0x00270D26u, 0xFFFFFFFFu, 0x30, 0, BCSVValueType.Long),
            new("SW_B", 0x00270D27u, 0xFFFFFFFFu, 0x34, 0, BCSVValueType.Long),
            new("DemoSkip", 0x36E91222u, 0xFFFFFFFFu, 0x38, 0, BCSVValueType.Long),
            new("pos_x", 0x065E794Du, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Float),
            new("pos_y", 0x065E794Eu, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Float),
            new("pos_z", 0x065E794Fu, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Float),
            new("dir_x", 0x05B2A146u, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Float),
            new("dir_y", 0x05B2A147u, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Float),
            new("dir_z", 0x05B2A148u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Float),
            new("scale_x", 0x71E5EAC3u, 0xFFFFFFFFu, 0x18, 0, BCSVValueType.Float),
            new("scale_y", 0x71E5EAC4u, 0xFFFFFFFFu, 0x1C, 0, BCSVValueType.Float),
            new("scale_z", 0x71E5EAC5u, 0xFFFFFFFFu, 0x20, 0, BCSVValueType.Float),
        ],
    };

    private static BCSVTable StartInfo() => new()
    {
        Rows = [],
        EntrySize = 0x34,
        DataOffset = 0xAC,
        Fields =
        [
            new("name", 0x00337A8Bu, 0xFFFFFFFFu, 0x30, 0, BCSVValueType.StringOffset),
            new("MarioNo", 0x953DC3C5u, 0xFFFFFFFFu, 0x24, 0, BCSVValueType.Long),
            new("Obj_arg0", 0x08E9C302u, 0xFFFFFFFFu, 0x28, 0, BCSVValueType.Long),
            new("Camera_id", 0x630FC055u, 0xFFFFFFFFu, 0x2C, 0, BCSVValueType.Long),
            new("pos_x", 0x065E794Du, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Float),
            new("pos_y", 0x065E794Eu, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Float),
            new("pos_z", 0x065E794Fu, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Float),
            new("dir_x", 0x05B2A146u, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Float),
            new("dir_y", 0x05B2A147u, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Float),
            new("dir_z", 0x05B2A148u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Float),
            new("scale_x", 0x71E5EAC3u, 0xFFFFFFFFu, 0x18, 0, BCSVValueType.Float),
            new("scale_y", 0x71E5EAC4u, 0xFFFFFFFFu, 0x1C, 0, BCSVValueType.Float),
            new("scale_z", 0x71E5EAC5u, 0xFFFFFFFFu, 0x20, 0, BCSVValueType.Float),
        ],
    };

    private static BCSVTable StageObjInfo() => new()
    {
        Rows = [],
        EntrySize = 0x20,
        DataOffset = 0x70,
        Fields =
        [
            new("name", 0x00337A8Bu, 0xFFFFFFFFu, 0x1C, 0, BCSVValueType.StringOffset),
            new("l_id", 0x003289CEu, 0xFFFFFFFFu, 0x18, 0, BCSVValueType.Long),
            new("pos_x", 0x065E794Du, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Float),
            new("pos_y", 0x065E794Eu, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Float),
            new("pos_z", 0x065E794Fu, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Float),
            new("dir_x", 0x05B2A146u, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Float),
            new("dir_y", 0x05B2A147u, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Float),
            new("dir_z", 0x05B2A148u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Float),
        ],
    };

    public static BCSVTable CommonPathInfo() => new()
    {
        Rows = [],
        EntrySize = 0x3C,
        DataOffset = 0xD0,
        Fields =
        [
            new("name", 0x00337A8Bu, 0xFFFFFFFFu, 0x2C, 0, BCSVValueType.StringOffset),
            new("type", 0x00368F3Au, 0xFFFFFFFFu, 0x30, 0, BCSVValueType.StringOffset),
            new("closed", 0xAF15E16Cu, 0xFFFFFFFFu, 0x34, 0, BCSVValueType.StringOffset),
            new("num_pnt", 0x88C159FDu, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Long),
            new("l_id", 0x003289CEu, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Long),
            new("path_arg0", 0xE975F474u, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Long),
            new("path_arg1", 0xE975F475u, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Long),
            new("path_arg2", 0xE975F476u, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Long),
            new("path_arg3", 0xE975F477u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Long),
            new("path_arg4", 0xE975F478u, 0xFFFFFFFFu, 0x18, 0, BCSVValueType.Long),
            new("path_arg5", 0xE975F479u, 0xFFFFFFFFu, 0x1C, 0, BCSVValueType.Long),
            new("path_arg6", 0xE975F47Au, 0xFFFFFFFFu, 0x20, 0, BCSVValueType.Long),
            new("path_arg7", 0xE975F47Bu, 0xFFFFFFFFu, 0x24, 0, BCSVValueType.Long),
            new("usage", 0x06A67DA1u, 0xFFFFFFFFu, 0x38, 0, BCSVValueType.StringOffset),
            new("no", 0x00000DC1u, 0x0000FFFFu, 0x28, 0, BCSVValueType.Short),
            new("Path_ID", 0x340BF355u, 0x0000FFFFu, 0x2A, 0, BCSVValueType.Short),
        ],
    };

    public static BCSVTable CommonPathPointInfo() => new()
    {
        Rows = [],
        EntrySize = 0x48,
        DataOffset = 0xE8,
        Fields =
        [
            new("point_arg0", 0x4B712DE9u, 0xFFFFFFFFu, 0x24, 0, BCSVValueType.Long),
            new("point_arg1", 0x4B712DEAu, 0xFFFFFFFFu, 0x28, 0, BCSVValueType.Long),
            new("point_arg2", 0x4B712DEBu, 0xFFFFFFFFu, 0x2C, 0, BCSVValueType.Long),
            new("point_arg3", 0x4B712DECu, 0xFFFFFFFFu, 0x30, 0, BCSVValueType.Long),
            new("point_arg4", 0x4B712DEDu, 0xFFFFFFFFu, 0x34, 0, BCSVValueType.Long),
            new("point_arg5", 0x4B712DEEu, 0xFFFFFFFFu, 0x38, 0, BCSVValueType.Long),
            new("point_arg6", 0x4B712DEFu, 0xFFFFFFFFu, 0x3C, 0, BCSVValueType.Long),
            new("point_arg7", 0x4B712DF0u, 0xFFFFFFFFu, 0x40, 0, BCSVValueType.Long),
            new("pnt0_x", 0xC5625A33u, 0xFFFFFFFFu, 0x0, 0, BCSVValueType.Float),
            new("pnt0_y", 0xC5625A34u, 0xFFFFFFFFu, 0x4, 0, BCSVValueType.Float),
            new("pnt0_z", 0xC5625A35u, 0xFFFFFFFFu, 0x8, 0, BCSVValueType.Float),
            new("pnt1_x", 0xC5625DF4u, 0xFFFFFFFFu, 0xC, 0, BCSVValueType.Float),
            new("pnt1_y", 0xC5625DF5u, 0xFFFFFFFFu, 0x10, 0, BCSVValueType.Float),
            new("pnt1_z", 0xC5625DF6u, 0xFFFFFFFFu, 0x14, 0, BCSVValueType.Float),
            new("pnt2_x", 0xC56261B5u, 0xFFFFFFFFu, 0x18, 0, BCSVValueType.Float),
            new("pnt2_y", 0xC56261B6u, 0xFFFFFFFFu, 0x1C, 0, BCSVValueType.Float),
            new("pnt2_z", 0xC56261B7u, 0xFFFFFFFFu, 0x20, 0, BCSVValueType.Float),
            new("id", 0x00000D1Bu, 0x0000FFFFu, 0x44, 0, BCSVValueType.Short),
        ],
    };
}
