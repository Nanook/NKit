using Nanook.NKit;
using System;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class TaskStepsExtractTests
    {
        //                  System,      srcFormat,   srcInfo,    extract,              Cfg,          outType        Result
        [Theory]
        [InlineData("001", "dreamcast", ".cue", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("002", "dreamcast", ".cue", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("003", "dreamcast", ".gdi", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("004", "dreamcast", ".chd", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("005", "dreamcast", ".chd", "idx", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("006", "wiiu", ".tmd", "", "ri:^.*", "", "folderfiles", "Extract-WiiU(M,V:NoVerify,C:ExtractWiiUStep)")]
        [InlineData("007", "wiiu", ".iso", "", "ri:^.*", "", "folderfiles", "Extract-WiiU(M,V:NoVerify,C:ExtractWiiUStep)")]
        [InlineData("008", "wiiu", ".wux", "", "ri:^.*", "", "folderfiles", "Extract-WiiU(M,V:NoVerify,C:ExtractWiiUStep)")]
        [InlineData("009", "ps3", ".dec.iso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("010", "ps3", ".cso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("011", "ps3", ".cso", "nkit", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("012", "ps3", ".zso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("013", "ps3", ".zso", "nkit", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("014", "ps3", ".iso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("015", "ps3", ".chd", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("016", "ps2", ".cso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("017", "ps2", ".cso", "nkit", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("018", "ps2", ".zso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("019", "ps2", ".zso", "nkit", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("020", "ps2", ".cue", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("021", "ps2", ".iso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("022", "ps2", ".chd", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("023", "ps2", ".chd", "idx", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("024", "ps1", ".cso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("025", "ps1", ".cso", "nkit", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("026", "ps1", ".zso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("027", "ps1", ".zso", "nkit", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("028", "ps1", ".cue", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("029", "ps1", ".iso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("030", "ps1", ".chd", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("031", "ps1", ".chd", "idx", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("032", "psp", ".cso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("033", "psp", ".cso", "nkit", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("034", "psp", ".zso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("035", "psp", ".zso", "nkit", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("036", "psp", ".jso", "nkit", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("037", "psp", ".dax", "nkit", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("038", "psp", ".iso", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("039", "psp", ".chd", "", "ri:^.*", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("040", "gamecube", ".iso", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("041", "gamecube", ".iso.dec", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("042", "gamecube", ".gcz", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("043", "gamecube", ".ciso", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("044", "gamecube", ".ciso", "nkit", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("045", "gamecube", ".wbfs", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("046", "gamecube", ".wbfs", "nkit", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("047", "gamecube", ".wia", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("048", "gamecube", ".rvz", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("049", "gamecube", ".rvz", "nkit", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("050", "gamecube", ".nkit.iso", "nkit", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("051", "gamecube", ".nkit.gcz", "nkit", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("052", "wii", ".iso", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("053", "wii", ".iso.dec", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("054", "wii", ".gcz", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("055", "wii", ".ciso", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("056", "wii", ".ciso", "nkit", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("057", "wii", ".wbfs", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("058", "wii", ".wbfs", "nkit", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("059", "wii", ".wia", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("060", "wii", ".rvz", "", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("061", "wii", ".rvz", "nkit", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("062", "wii", ".nkit.iso", "nkit", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("063", "wii", ".nkit.gcz", "nkit", "ri:^.*", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("064", "dreamcast", ".cue", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("065", "dreamcast", ".cue", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("066", "dreamcast", ".chd", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("067", "dreamcast", ".chd", "idx", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("068", "wiiu", ".tmd", "", "f", "", "folderfiles", "Extract-WiiU(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("069", "wiiu", ".iso", "", "f", "", "folderfiles", "Extract-WiiU(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("070", "wiiu", ".wux", "", "f", "", "folderfiles", "Extract-WiiU(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("071", "ps3", ".dec.iso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("072", "ps3", ".cso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("073", "ps3", ".cso", "nkit", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("074", "ps3", ".zso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("075", "ps3", ".zso", "nkit", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("076", "ps3", ".iso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("077", "ps3", ".chd", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("078", "ps2", ".cso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("079", "ps2", ".cso", "nkit", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("080", "ps2", ".zso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("081", "ps2", ".zso", "nkit", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("082", "ps2", ".cue", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("083", "ps2", ".iso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("084", "ps2", ".chd", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("085", "ps1", ".cso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("086", "ps1", ".cso", "nkit", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("087", "ps1", ".zso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("088", "ps1", ".zso", "nkit", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("089", "ps1", ".cue", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("090", "ps1", ".iso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("091", "ps1", ".chd", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("092", "ps1", ".chd", "idx", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("093", "psp", ".cso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("094", "psp", ".cso", "nkit", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("095", "psp", ".zso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("096", "psp", ".zso", "nkit", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("097", "psp", ".jso", "nkit", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("098", "psp", ".dax", "nkit", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("099", "psp", ".iso", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("100", "psp", ".chd", "", "f", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("101", "gamecube", ".iso", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("102", "gamecube", ".iso.dec", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("103", "gamecube", ".gcz", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("104", "gamecube", ".ciso", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("105", "gamecube", ".ciso", "nkit", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("106", "gamecube", ".wbfs", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("107", "gamecube", ".wbfs", "nkit", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("108", "gamecube", ".wia", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("109", "gamecube", ".rvz", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("110", "gamecube", ".rvz", "nkit", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("111", "gamecube", ".nkit.iso", "nkit", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("112", "gamecube", ".nkit.gcz", "nkit", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("113", "wii", ".iso", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("114", "wii", ".iso.dec", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("115", "wii", ".gcz", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("116", "wii", ".ciso", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("117", "wii", ".ciso", "nkit", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("118", "wii", ".wbfs", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("119", "wii", ".wbfs", "nkit", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("120", "wii", ".wia", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("121", "wii", ".rvz", "", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("122", "wii", ".rvz", "nkit", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("123", "wii", ".nkit.iso", "nkit", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("124", "wii", ".nkit.gcz", "nkit", "f", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractForensicStep)")]
        [InlineData("125", "saturn", ".chd", "idx", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("126", "segacd", ".chd", "idx", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("127", "cdi", ".chd", "idx", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("128", "xbox", ".chd", "", "", "", "folderfiles", "Extract-XBox(M,V:NoVerify,C:ExtractXBoxStep)")]
        [InlineData("129", "xbox360", ".chd", "", "", "", "folderfiles", "Extract-XBox(M,V:NoVerify,C:ExtractXBoxStep)")]
        [InlineData("130", "default", ".chd", "idx", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("131", "default", ".chd", "", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("132", "wii", ".chd", "", "", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("133", "gamecube", ".chd", "", "", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("134", "wiiu", ".chd", "", "", "", "folderfiles", "Extract-WiiU(M,V:NoVerify,C:ExtractWiiUStep)")]
        [InlineData("135", "ps1", ".nkds", "idxds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("136", "ps2", ".nkds", "idxds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("137", "ps2", ".nkds", "ds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("138", "ps3", ".nkds", "ds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("139", "psp", ".nkds", "ds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("140", "dreamcast", ".nkds", "idxds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("141", "saturn", ".nkds", "idxds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("142", "segacd", ".nkds", "idxds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("143", "cdi", ".nkds", "idxds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("144", "xbox", ".nkds", "ds", "", "", "folderfiles", "Extract-XBox(M,V:NoVerify,C:ExtractXBoxStep)")]
        [InlineData("145", "xbox360", ".nkds", "ds", "", "", "folderfiles", "Extract-XBox(M,V:NoVerify,C:ExtractXBoxStep)")]
        [InlineData("146", "default", ".nkds", "idxds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("147", "default", ".nkds", "ds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("148", "wii", ".nkds", "ds", "", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("149", "gamecube", ".nkds", "ds", "", "", "folderfiles", "Extract-WiiGc(M,V:NoVerify,C:ExtractWiiGcStep)")]
        [InlineData("150", "wiiu", ".nkds", "ds", "", "", "folderfiles", "Extract-WiiU(M,V:NoVerify,C:ExtractWiiUStep)")]
        [InlineData("151", "pcEngine", ".chd", "idx", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        [InlineData("152", "pcEngine", ".nkds", "idxds", "", "", "folderfiles", "Extract-Iso(M,V:NoVerify,C:ExtractIsoStep)")]
        public void ExtractTest(string idx, string system, string srcFormat, string srcInfo, string extract, string cfg, string outType, string resultString)
        {
            TaskStepVerifySettings[] vfy = TaskStepsShared.VerifyCombos();

            for (int v = 0; v < vfy.Length; v++)
            {
                string taskType = "extract";
                // ReqPatch is only set by the WiiGc reader for the Expand task.
                // Extract reads the corrected nkit image in a single pass, so ReqPatch is never set.
                bool reqPatch = false;
                bool nkitHeader = srcInfo.Contains("nkit");
                bool srcCrcHash = nkitHeader;

                IParts parts = new Parts(0); //none for extract

                NKitTaskContext task = TaskStepsShared.Process(taskType, system, srcFormat, srcInfo, extract, reqPatch, vfy[v].PrmV, parts, vfy[v].InNKitScan, vfy[v].Dats, vfy[v].DatItem, cfg);
                TaskStepResult[] results = TaskStepsShared.GetResults(resultString);
                int i = 0;

                Assert.Equal(results.Length, task.Steps.Count);
                Assert.Equal(1, results.Count(a => a.IsMain));
                foreach (NKitStepContext step in task.Steps)
                {
                    NKitVerify.RequiredChecksums(vfy[v].Dats, parts, step.StepInfo, out string chkString);
                    Assert.Equal(results[i].Name, step.StepInfo.Name);
                    if (step.StepInfo.Name != "NotSet-NotSupported")
                    {
                        if (results[i].IsMain)
                        {
                            Assert.Equal(cfg.ToLower(), step.StepInfo.ImageConfig.ToLower());
                            if (step.StepInfo.WriteImage)
                                Assert.Equal(outType.ToLower(), step.StepInfo.OutputType.ToString().ToLower());
                        }
                        if (results[i].IsVerify)
                            Assert.Equal(results[i].VerifyType, chkString);
                        Assert.Equal(results[i].ClassName, step.Step.GetType().Name);
                    }
                    i++;
                }
            }
        }


        //[Theory]
        ////               Task,     System,    Config,  ReqPatch, PrmV,        SrcCrcHash, InNKitScan, Dats,   DatItem  Result
        //[InlineData(  1, "Dedupe", "wii",       "",      false,    false,   "Dedupe-Image,Verify-Image")]
        //[InlineData(  1, "Dedupe", "gamecube","",      false,    false,   "Dedupe-Image,Verify-Image")]
        //[InlineData(  1, "Dedupe", "wii",       "",      false,    false,   "Dedupe-Image,Verify-Image")]
        //[InlineData(  1, "Dedupe", "gamecube","",      false,    false,   "Dedupe-Image,Verify-Image")]
        //[InlineData(  2, "Dedupe", "wii",       "",      false,    false,   "Dedupe-Image")]
        //public void TestSteps(int idx, string taskType, string system, string config, bool reqPatch, string prmV, bool srcCrcHash, bool inScan, bool dats, bool datItem, string results)
        //{
        //    Checksums
        //    NKitTaskContext task = process(taskType, system, config, reqPatch, prmV, srcCrcHash, inScan, dats, datItem);
        //    Assert.Equal(results, string.Join(",", task.Steps.Select(a => a.StepInfo.Name)));
        //}
    }
}