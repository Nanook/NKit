using Nanook.NKit;
using System;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class TaskStepsFixTests
    {
        //                  System,      srcFormat,   NKitHeader, convert,              PrmV,        InNKitScan, Dats,   DatItem  Cfg,          outType        Result
        #region FixGamecube_Iso
        [Theory]
        [InlineData("001", "gamecube", ".iso", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".iso", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".iso", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".iso", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".iso", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".iso", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".iso", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".iso", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".iso", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".iso", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".iso", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".iso", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".iso", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".iso", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".iso", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".iso", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".iso", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".iso", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_Iso(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_IsoDec
        [Theory]
        [InlineData("001", "gamecube", ".iso.dec", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".iso.dec", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".iso.dec", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".iso.dec", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".iso.dec", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".iso.dec", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".iso.dec", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".iso.dec", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".iso.dec", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".iso.dec", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".iso.dec", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".iso.dec", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".iso.dec", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".iso.dec", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".iso.dec", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".iso.dec", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".iso.dec", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".iso.dec", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_IsoDec(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_Gcz
        [Theory]
        [InlineData("001", "gamecube", ".gcz", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".gcz", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".gcz", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".gcz", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".gcz", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".gcz", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".gcz", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".gcz", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".gcz", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".gcz", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".gcz", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".gcz", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".gcz", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".gcz", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".gcz", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".gcz", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".gcz", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".gcz", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_Gcz(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_Ciso
        [Theory]
        [InlineData("001", "gamecube", ".ciso", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".ciso", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".ciso", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".ciso", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".ciso", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".ciso", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".ciso", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".ciso", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".ciso", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".ciso", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".ciso", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".ciso", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".ciso", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".ciso", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".ciso", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".ciso", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".ciso", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".ciso", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_Ciso(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_CisoNKit
        [Theory]
        [InlineData("001", "gamecube", ".ciso", "nkit", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".ciso", "nkit", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".ciso", "nkit", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".ciso", "nkit", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".ciso", "nkit", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".ciso", "nkit", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".ciso", "nkit", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".ciso", "nkit", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".ciso", "nkit", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".ciso", "nkit", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".ciso", "nkit", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".ciso", "nkit", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".ciso", "nkit", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".ciso", "nkit", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".ciso", "nkit", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".ciso", "nkit", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".ciso", "nkit", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".ciso", "nkit", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_CisoNKit(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_Wbfs
        [Theory]
        [InlineData("001", "gamecube", ".wbfs", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".wbfs", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".wbfs", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".wbfs", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".wbfs", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".wbfs", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".wbfs", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".wbfs", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".wbfs", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".wbfs", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".wbfs", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".wbfs", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".wbfs", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".wbfs", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".wbfs", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".wbfs", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".wbfs", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".wbfs", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_Wbfs(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_WbfsNKit
        [Theory]
        [InlineData("001", "gamecube", ".wbfs", "nkit", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".wbfs", "nkit", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".wbfs", "nkit", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".wbfs", "nkit", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".wbfs", "nkit", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".wbfs", "nkit", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".wbfs", "nkit", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".wbfs", "nkit", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".wbfs", "nkit", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".wbfs", "nkit", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".wbfs", "nkit", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".wbfs", "nkit", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".wbfs", "nkit", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".wbfs", "nkit", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".wbfs", "nkit", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".wbfs", "nkit", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".wbfs", "nkit", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".wbfs", "nkit", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_WbfsNKit(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_Wia
        [Theory]
        [InlineData("001", "gamecube", ".wia", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".wia", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".wia", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".wia", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".wia", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".wia", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".wia", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".wia", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".wia", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".wia", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".wia", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".wia", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".wia", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".wia", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".wia", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".wia", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".wia", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".wia", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_Wia(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_Rvz
        [Theory]
        [InlineData("001", "gamecube", ".rvz", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".rvz", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".rvz", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".rvz", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".rvz", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".rvz", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".rvz", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".rvz", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".rvz", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".rvz", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".rvz", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".rvz", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".rvz", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".rvz", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".rvz", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".rvz", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".rvz", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".rvz", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_Rvz(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_RvzNKit
        [Theory]
        [InlineData("001", "gamecube", ".rvz", "nkit", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".rvz", "nkit", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".rvz", "nkit", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".rvz", "nkit", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".rvz", "nkit", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".rvz", "nkit", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".rvz", "nkit", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".rvz", "nkit", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".rvz", "nkit", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".rvz", "nkit", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".rvz", "nkit", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".rvz", "nkit", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".rvz", "nkit", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".rvz", "nkit", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".rvz", "nkit", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".rvz", "nkit", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".rvz", "nkit", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".rvz", "nkit", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_RvzNKit(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_NKitIso
        [Theory]
        [InlineData("001", "gamecube", ".nkit.iso", "nkit", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".nkit.iso", "nkit", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".nkit.iso", "nkit", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".nkit.iso", "nkit", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".nkit.iso", "nkit", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".nkit.iso", "nkit", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".nkit.iso", "nkit", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".nkit.iso", "nkit", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".nkit.iso", "nkit", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".nkit.iso", "nkit", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".nkit.iso", "nkit", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".nkit.iso", "nkit", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".nkit.iso", "nkit", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".nkit.iso", "nkit", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".nkit.iso", "nkit", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".nkit.iso", "nkit", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".nkit.iso", "nkit", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".nkit.iso", "nkit", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_NKitIso(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixPs3_Sbf
        [Theory]
        [InlineData("001", "ps3", ".sfb", "", "iso", "n", false, false, false, "sfb", "image", "Fix-Ps3(M,V:NoVerify,C:FixPs3IrdStep)")]
        [InlineData("002", "ps3", ".sfb", "", "iso", "n", false, true, false, "sfb", "image", "Fix-Ps3(M,V:NoVerify,C:FixPs3IrdStep)")]
        [InlineData("003", "ps3", ".sfb", "", "iso", "n", false, true, true, "sfb", "image", "Fix-Ps3(M,V:NoVerify,C:FixPs3IrdStep)")]
        [InlineData("004", "ps3", ".sfb", "", "iso", "n", true, false, false, "sfb", "image", "Fix-Ps3(M,V:NoVerify,C:FixPs3IrdStep)")]
        [InlineData("005", "ps3", ".sfb", "", "iso", "n", true, true, false, "sfb", "image", "Fix-Ps3(M,V:NoVerify,C:FixPs3IrdStep)")]
        [InlineData("006", "ps3", ".sfb", "", "iso", "n", true, true, true, "sfb", "image", "Fix-Ps3(M,V:NoVerify,C:FixPs3IrdStep)")]
        [InlineData("007", "ps3", ".sfb", "", "iso", "y", false, false, false, "sfb", "image", "Fix-Ps3(M,V:NoVerify,C:FixPs3IrdStep)")]
        [InlineData("008", "ps3", ".sfb", "", "iso", "y", false, true, false, "sfb", "image", "Fix-Ps3(M,V:DatLookup [Md5+Crc32],C:FixPs3IrdStep)")]
        [InlineData("009", "ps3", ".sfb", "", "iso", "y", false, true, true, "sfb", "image", "Fix-Ps3(M,V:DatMatch [Md5+Crc32],C:FixPs3IrdStep)")]
        [InlineData("010", "ps3", ".sfb", "", "iso", "y", true, false, false, "sfb", "image", "Fix-Ps3(M,V:ScanCompare [Crc32Only],C:FixPs3IrdStep)")]
        [InlineData("011", "ps3", ".sfb", "", "iso", "y", true, true, false, "sfb", "image", "Fix-Ps3(M,V:ScanCompare [Crc32Only],C:FixPs3IrdStep)")]
        [InlineData("012", "ps3", ".sfb", "", "iso", "y", true, true, true, "sfb", "image", "Fix-Ps3(M,V:ScanCompare [Crc32Only],C:FixPs3IrdStep)")]
        [InlineData("013", "ps3", ".sfb", "", "iso", "datLookup", false, false, false, "sfb", "image", "Fix-Ps3(M,V:NoVerify,C:FixPs3IrdStep)")]
        [InlineData("014", "ps3", ".sfb", "", "iso", "datLookup", false, true, false, "sfb", "image", "Fix-Ps3(M,V:DatLookup [Md5+Crc32],C:FixPs3IrdStep)")]
        [InlineData("015", "ps3", ".sfb", "", "iso", "datLookup", false, true, true, "sfb", "image", "Fix-Ps3(M,V:DatLookup [Md5+Crc32],C:FixPs3IrdStep)")]
        [InlineData("016", "ps3", ".sfb", "", "iso", "datLookup", true, false, false, "sfb", "image", "Fix-Ps3(M,V:NoVerify,C:FixPs3IrdStep)")]
        [InlineData("017", "ps3", ".sfb", "", "iso", "datLookup", true, true, false, "sfb", "image", "Fix-Ps3(M,V:DatLookup [Md5+Crc32],C:FixPs3IrdStep)")]
        [InlineData("018", "ps3", ".sfb", "", "iso", "datLookup", true, true, true, "sfb", "image", "Fix-Ps3(M,V:DatLookup [Md5+Crc32],C:FixPs3IrdStep)")]
        public void FixPs3_Sbf(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_NKitGcz
        [Theory]
        [InlineData("001", "gamecube", ".nkit.gcz", "nkit", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".nkit.gcz", "nkit", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".nkit.gcz", "nkit", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".nkit.gcz", "nkit", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".nkit.gcz", "nkit", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".nkit.gcz", "nkit", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".nkit.gcz", "nkit", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".nkit.gcz", "nkit", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".nkit.gcz", "nkit", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".nkit.gcz", "nkit", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".nkit.gcz", "nkit", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".nkit.gcz", "nkit", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".nkit.gcz", "nkit", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".nkit.gcz", "nkit", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".nkit.gcz", "nkit", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".nkit.gcz", "nkit", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".nkit.gcz", "nkit", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".nkit.gcz", "nkit", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_NKitGcz(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_Iso
        [Theory]
        [InlineData("001", "wii", ".iso", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".iso", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".iso", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".iso", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".iso", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".iso", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".iso", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".iso", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".iso", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".iso", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".iso", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".iso", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".iso", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".iso", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".iso", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".iso", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".iso", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".iso", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_Iso(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_IsoDec
        [Theory]
        [InlineData("001", "wii", ".iso.dec", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".iso.dec", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".iso.dec", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".iso.dec", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".iso.dec", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".iso.dec", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".iso.dec", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".iso.dec", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".iso.dec", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".iso.dec", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".iso.dec", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".iso.dec", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".iso.dec", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".iso.dec", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".iso.dec", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".iso.dec", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".iso.dec", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".iso.dec", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_IsoDec(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_Gcz
        [Theory]
        [InlineData("001", "wii", ".gcz", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".gcz", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".gcz", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".gcz", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".gcz", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".gcz", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".gcz", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".gcz", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".gcz", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".gcz", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".gcz", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".gcz", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".gcz", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".gcz", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".gcz", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".gcz", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".gcz", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".gcz", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_Gcz(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_Ciso
        [Theory]
        [InlineData("001", "wii", ".ciso", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".ciso", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".ciso", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".ciso", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".ciso", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".ciso", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".ciso", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".ciso", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".ciso", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".ciso", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".ciso", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".ciso", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".ciso", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".ciso", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".ciso", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".ciso", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".ciso", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".ciso", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_Ciso(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_CisoNKit
        [Theory]
        [InlineData("001", "wii", ".ciso", "nkit", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".ciso", "nkit", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".ciso", "nkit", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".ciso", "nkit", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".ciso", "nkit", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".ciso", "nkit", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".ciso", "nkit", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".ciso", "nkit", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".ciso", "nkit", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".ciso", "nkit", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".ciso", "nkit", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".ciso", "nkit", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".ciso", "nkit", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".ciso", "nkit", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".ciso", "nkit", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".ciso", "nkit", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".ciso", "nkit", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".ciso", "nkit", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_CisoNKit(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_Wbfs
        [Theory]
        [InlineData("001", "wii", ".wbfs", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".wbfs", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".wbfs", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".wbfs", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".wbfs", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".wbfs", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".wbfs", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".wbfs", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".wbfs", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".wbfs", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".wbfs", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".wbfs", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".wbfs", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".wbfs", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".wbfs", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".wbfs", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".wbfs", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".wbfs", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_Wbfs(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_WbfsNKit
        [Theory]
        [InlineData("001", "wii", ".wbfs", "nkit", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".wbfs", "nkit", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".wbfs", "nkit", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".wbfs", "nkit", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".wbfs", "nkit", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".wbfs", "nkit", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".wbfs", "nkit", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".wbfs", "nkit", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".wbfs", "nkit", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".wbfs", "nkit", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".wbfs", "nkit", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".wbfs", "nkit", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".wbfs", "nkit", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".wbfs", "nkit", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".wbfs", "nkit", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".wbfs", "nkit", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".wbfs", "nkit", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".wbfs", "nkit", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_WbfsNKit(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_Wia
        [Theory]
        [InlineData("001", "wii", ".wia", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".wia", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".wia", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".wia", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".wia", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".wia", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".wia", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".wia", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".wia", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".wia", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".wia", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".wia", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".wia", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".wia", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".wia", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".wia", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".wia", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".wia", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_Wia(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_Rvz
        [Theory]
        [InlineData("001", "wii", ".rvz", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".rvz", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".rvz", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".rvz", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".rvz", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".rvz", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".rvz", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".rvz", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".rvz", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".rvz", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".rvz", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".rvz", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".rvz", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".rvz", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".rvz", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".rvz", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".rvz", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".rvz", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_Rvz(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_RvzNKit
        [Theory]
        [InlineData("001", "wii", ".rvz", "nkit", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".rvz", "nkit", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".rvz", "nkit", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".rvz", "nkit", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".rvz", "nkit", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".rvz", "nkit", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".rvz", "nkit", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".rvz", "nkit", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".rvz", "nkit", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".rvz", "nkit", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".rvz", "nkit", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".rvz", "nkit", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".rvz", "nkit", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".rvz", "nkit", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".rvz", "nkit", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".rvz", "nkit", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".rvz", "nkit", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".rvz", "nkit", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_RvzNKit(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_NKitIso
        [Theory]
        [InlineData("001", "wii", ".nkit.iso", "nkit", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".nkit.iso", "nkit", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".nkit.iso", "nkit", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".nkit.iso", "nkit", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".nkit.iso", "nkit", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".nkit.iso", "nkit", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".nkit.iso", "nkit", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".nkit.iso", "nkit", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".nkit.iso", "nkit", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".nkit.iso", "nkit", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".nkit.iso", "nkit", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".nkit.iso", "nkit", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".nkit.iso", "nkit", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".nkit.iso", "nkit", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".nkit.iso", "nkit", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".nkit.iso", "nkit", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".nkit.iso", "nkit", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".nkit.iso", "nkit", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_NKitIso(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixWii_NKitGcz
        [Theory]
        [InlineData("001", "wii", ".nkit.gcz", "nkit", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".nkit.gcz", "nkit", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".nkit.gcz", "nkit", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".nkit.gcz", "nkit", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".nkit.gcz", "nkit", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".nkit.gcz", "nkit", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".nkit.gcz", "nkit", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".nkit.gcz", "nkit", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".nkit.gcz", "nkit", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".nkit.gcz", "nkit", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".nkit.gcz", "nkit", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".nkit.gcz", "nkit", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".nkit.gcz", "nkit", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".nkit.gcz", "nkit", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".nkit.gcz", "nkit", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".nkit.gcz", "nkit", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".nkit.gcz", "nkit", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".nkit.gcz", "nkit", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_NKitGcz(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion

        #region FixWii_Chd
        [Theory]
        [InlineData("001", "wii", ".chd", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".chd", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".chd", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".chd", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".chd", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".chd", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".chd", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".chd", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".chd", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".chd", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".chd", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".chd", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".chd", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".chd", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".chd", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".chd", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".chd", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".chd", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_Chd(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_Chd
        [Theory]
        [InlineData("001", "gamecube", ".chd", "", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".chd", "", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".chd", "", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".chd", "", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".chd", "", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".chd", "", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".chd", "", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".chd", "", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".chd", "", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".chd", "", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".chd", "", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".chd", "", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".chd", "", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".chd", "", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".chd", "", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".chd", "", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".chd", "", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".chd", "", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_Chd(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion

        #region FixWii_Nkds
        [Theory]
        [InlineData("001", "wii", ".nkds", "ds", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "wii", ".nkds", "ds", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "wii", ".nkds", "ds", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "wii", ".nkds", "ds", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "wii", ".nkds", "ds", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "wii", ".nkds", "ds", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "wii", ".nkds", "ds", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "wii", ".nkds", "ds", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("009", "wii", ".nkds", "ds", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("010", "wii", ".nkds", "ds", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "wii", ".nkds", "ds", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("012", "wii", ".nkds", "ds", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("013", "wii", ".nkds", "ds", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "wii", ".nkds", "ds", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("015", "wii", ".nkds", "ds", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("016", "wii", ".nkds", "ds", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "wii", ".nkds", "ds", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        [InlineData("018", "wii", ".nkds", "ds", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)|Verify-Image(V:DatLookup [Md5+Crc32],C:ScanStep)")]
        public void FixWii_Nkds(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion
        #region FixGamecube_Nkds
        [Theory]
        [InlineData("001", "gamecube", ".nkds", "ds", "iso", "n", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("002", "gamecube", ".nkds", "ds", "iso", "n", false, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("003", "gamecube", ".nkds", "ds", "iso", "n", false, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("004", "gamecube", ".nkds", "ds", "iso", "n", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("005", "gamecube", ".nkds", "ds", "iso", "n", true, true, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("006", "gamecube", ".nkds", "ds", "iso", "n", true, true, true, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("007", "gamecube", ".nkds", "ds", "iso", "y", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("008", "gamecube", ".nkds", "ds", "iso", "y", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("009", "gamecube", ".nkds", "ds", "iso", "y", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("010", "gamecube", ".nkds", "ds", "iso", "y", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("011", "gamecube", ".nkds", "ds", "iso", "y", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("012", "gamecube", ".nkds", "ds", "iso", "y", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("013", "gamecube", ".nkds", "ds", "iso", "datLookup", false, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("014", "gamecube", ".nkds", "ds", "iso", "datLookup", false, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("015", "gamecube", ".nkds", "ds", "iso", "datLookup", false, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("016", "gamecube", ".nkds", "ds", "iso", "datLookup", true, false, false, "", "image", "Fix-WiiGc(M,V:NoVerify,C:FixWiiGcStep)")]
        [InlineData("017", "gamecube", ".nkds", "ds", "iso", "datLookup", true, true, false, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        [InlineData("018", "gamecube", ".nkds", "ds", "iso", "datLookup", true, true, true, "", "image", "Fix-WiiGc(M,V:DatLookup [Md5+Crc32],C:FixWiiGcStep)")]
        public void FixGamecube_Nkds(string idx, string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString) => fixTest(system, srcFormat, srcInfo, convert, prmV, inScan, dats, datItem, cfg, outType, resultString);
        #endregion

        private void fixTest(string system, string srcFormat, string srcInfo, string convert, string prmV, bool inScan, bool dats, bool datItem, string cfg, string outType, string resultString)
        {
            string taskType = "fix";
            // ReqPatch is set by the WiiGc reader for the EXPAND task only. Fix never sets it:
            // Wii Fix reads the corrected nkit in a single pass (Fix-WiiGc), adding a second
            // Verify-Image step only when a verify is required (fix-to-disk then re-read);
            // GameCube fixes and verifies in one pass. So ReqPatch is always false for Fix.
            bool reqPatch = false;
            bool nkitHeader = srcInfo.Contains("nkit");
            bool srcCrcHash = nkitHeader;

            IParts parts = TaskStepsShared.CreateInChecksums(srcFormat, nkitHeader);

            NKitTaskContext task = TaskStepsShared.Process(taskType, system, srcFormat, srcInfo, convert, reqPatch, prmV, parts, inScan, dats, datItem, cfg);
            TaskStepResult[] results = TaskStepsShared.GetResults(resultString);
            int i = 0;

            Assert.Equal(results.Length, task.Steps.Count);
            Assert.Equal(1, results.Count(a => a.IsMain));
            foreach (NKitStepContext step in task.Steps)
            {
                NKitVerify.RequiredChecksums(dats, parts, step.StepInfo, out string chkString);
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
}