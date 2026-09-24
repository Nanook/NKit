using Nanook.NKit;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public class WipedImageTestCreator : WipedImageTestsBase
    {
        private const string _InPath = @"../../../../../WipedImages";
        private const string _Keys = _InPath + @"/_keys";
        private const string _FixPs3 = _InPath + @"/_fix/fix_ps3.yaml";
        private const string _FixFilesPs3 = _InPath + @"/_fix/ps3_files";
        private const string _DatWii = _InPath + @"/_dats/wii.dat";
        private const string _FixFilesWii = _InPath + @"/_fix/wii_files";

        //uncomment the line below to regenerate the tests. It will auto recomment itself upon competion

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Cdi_Cuvyvcf(string task, string taskOptions) =>
            createTests("Cdi", task, taskOptions, "Cuvyvcf Zrqvn Vagrenpgvrir Raplpybcrqvr (Argureynaqf).7z", "Retail     / CUE       / 742MiB  / CD-i Fs", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Cdi_Hygen(string task, string taskOptions) =>
            createTests("Cdi", task, taskOptions, "Hygen PQ-v Fbppre (Rhebcr).7z", "Retail     / CUE       / 18MiB   / CD-i Fs", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Cdi_PQV(string task, string taskOptions) =>
            createTests("Cdi", task, taskOptions, "PQ-V Zhfvp Obbx - Pynffvpny Thvgne Ibyhzr 6 (HFN).7z", "Retail     / CUE       / 118MiB  / CD-i Fs", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Default_Abk(string task, string taskOptions) =>
            createTests("Default", task, taskOptions, "Abk (Rhebcr) (Ra,Se,Qr,Rf,Vg) (Qvfp 8) (Znahnyf Qvfp) (Ereryrnfr).7z", "Default   / Retail     / Cue       / 107MiB  / Multisession, 2nd session replaces files", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Default_Pneaviberf(string task, string taskOptions) =>
            createTests("Default", task, taskOptions, "Pneaviberf (HFN).7z", "Retail     / CUE       / 111MiB  / Multisession, 2nd session refs session 1", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cso")]
        [InlineData("Convert", "cso2")]
        [InlineData("Convert", "zso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Default_Romeo(string task, string taskOptions) =>
            createTests("Default", task, taskOptions, "Romeo.7z", "Default   / Created    / ISO       / 600MiB  / Romeo Fs (IS09660 with lonf filename), 0 byte file", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cso")]
        [InlineData("Convert", "cso2")]
        [InlineData("Convert", "zso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Default_Sha(string task, string taskOptions) =>
            createTests("Default", task, taskOptions, "Sha Fpubby 2.7z", "Default   / Retail     / ISO       / 97.8MiB / CD-XA Joilet", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cso")]
        [InlineData("Convert", "cso2")]
        [InlineData("Convert", "zso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Default_Zvkryf(string task, string taskOptions) =>
            createTests("Default", task, taskOptions, "Zvkryf IPQ qhzc.7z", "Default   / Retail     / ISO       / 222MiB  / RockRidge", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cso")]
        [InlineData("Convert", "cso2")]
        [InlineData("Convert", "zso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Default_Zvpebfbsg(string task, string taskOptions) =>
            createTests("Default", task, taskOptions, "Zvpebfbsg Choyvfure 7555.7z", "Default   / Retail     / ISO       / 502MiB  / RockRidge", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Convert", "gdi")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Dreamcast_Anzpb(string task, string taskOptions) =>
            createTests("Dreamcast", task, taskOptions, "Anzpb Zhfrhz (HFN).chd", "Retail     / CHD       / 1.10GiB / Type3 Split (Data Audio Data Audio Data)", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Convert", "gdi")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Dreamcast_AnzpbGdi(string task, string taskOptions) =>
            createTests("Dreamcast", task, taskOptions, "Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba].7z", "Retail     / GDI       / 1.10GiB / Type3 Split (Data Audio Data Audio Data)", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Convert", "gdi")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Dreamcast_Furazhr(string task, string taskOptions) =>
            createTests("Dreamcast", task, taskOptions, "Furazhr VV (Wncna) (Qvfp 7).chd", "Retail     / CHD       / 1.10GiB / Type3 (Data Audio Data Data))", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Convert", "gdi")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Dreamcast_Iveghn(string task, string taskOptions) =>
            createTests("Dreamcast", task, taskOptions, "Iveghn Nguyrgr 7555 (HFN) (Ra,Se,Qr,Rf).chd", "Retail     / CHD       / 1.10GiB / Type1 (Data Audio Data)", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Dreamcast_Unat(string task, string taskOptions) =>
            createTests("Dreamcast", task, taskOptions, "Unat gur QW (Wncna).chd", "MILCD      / CHD       / 680MiB  / MIL CD (Audio... Data)", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Dreamcast_Unat2(string task, string taskOptions) =>
            createTests("Dreamcast", task, taskOptions, "Unat gur QW (Wncna).7z", "MILCD      / CHD       / 680MiB  / MIL CD (Audio... Data)", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Dreamcast_ZC8(string task, string taskOptions) =>
            createTests("Dreamcast", task, taskOptions, "ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay).7z", "Unlicenced / CHD       / 679MiB  / First track marked as data with no FS, 2nd is Dreamcast - 2 sessions", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Convert", "gdi")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Dreamcast_Zrzbevrf(string task, string taskOptions) =>
            createTests("Dreamcast", task, taskOptions, "Zrzbevrf Bss 7aq - Znxvat Qvfp (Wncna).chd", "Retail     / CHD       / 1.10GiB / Type2 (Data Audio Data Audio Audio)", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:n")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:n")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Gamecube_Jurryre(string task, string taskOptions) =>
            createTests("Gamecube", task, taskOptions, "63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf).rvz", "Retail     / RVZ       / 1.36GiB / Fst in 2nd section, Files at the start of the image", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:y")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:y")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Gamecube_Crnpu(string task, string taskOptions) =>
            createTests("Gamecube", task, taskOptions, "Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb.rvz", "Demo       / RVZ       / 1.36GiB / No Magic ID", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:n")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:n")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Gamecube_FreeLoader(string task, string taskOptions) =>
            createTests("Gamecube", task, taskOptions, "FreeLoader for GameCube (Europe) (Unl) (v1.04).rvz", "Unlicenced / RVZ       / 1.36GiB / Hacked", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:y")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:y")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Gamecube_Gbjre(string task, string taskOptions) =>
            createTests("Gamecube", task, taskOptions, "Gbjre bs Qehntn, Gur (Wncna).rvz", "Retail     / RVZ       / 1.36GiB / FS only has opening.boundary", null, null, null, null, true);

        //[Theory]
        [InlineData("Fix", "")]
        public void Gamecube_GbjreFix(string task, string taskOptions) =>
            createTests("Gamecube", task, taskOptions, "Gbjre bs Qehntn, Gur (Wncna) (nkitv1).zip", "Retail     / RVZ       / 1.36GiB / NKitv1 iso edited to look like a shrunk iso with edited header", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:y")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:y")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Gamecube_Uneirfg(string task, string taskOptions) =>
            createTests("Gamecube", task, taskOptions, "Uneirfg Zbba - N Jbaqreshy Yvsr (Rhebcr).rvz", "Retail     / RVZ       / 1.36GiB / 0 byte files", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:n")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:n")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Gamecube_FgneSbk506257(string task, string taskOptions) =>
            createTests("Gamecube", task, taskOptions, "mm_FgneSbk506257_r8.rvz", "Demo       / RVZ       / 803MiB  / Image size not a multiple of 4, ends with a 1 byte file", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Ps1_CbCbEbThr(string task, string taskOptions) =>
            createTests("Ps1", task, taskOptions, "CbCbEbThr (Wncna) (Eri 6).7z", "Retail     / CUE (7z)  / 670MiB  / Directory Enries that point to invalid indexes", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Ps1_Vqr(string task, string taskOptions) =>
            createTests("Ps1", task, taskOptions, "Vqr Lbhfhxr ab Znuwbat Xnmbxh (Wncna).zip", "Retail     / CUE (zip) / 527MiB  / Bad Date in Directory Entry", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "zso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Ps2_Ebpx(string task, string taskOptions) =>
            createTests("Ps2", task, taskOptions, "Ebpx Onaq 7 (HFN).7z", "Retail     / ISO       / 7.68GiB / 2 Images joined with no 2nd iso header", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cue")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Ps2_Penml(string task, string taskOptions) =>
            createTests("Ps2", task, taskOptions, "Penml Gnkv (HFN).zip", "Retail     / CUE       / 715MiB  / CD with UDF and ISO9660", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "cso")]
        [InlineData("Convert", "cso2")]
        [InlineData("Convert", "zso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Ps2_Vpb(string task, string taskOptions) =>
            createTests("Ps2", task, taskOptions, "Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg).7z", "Retail     / ISO       / 866MiB  / UDF NSR02, DVD with UDF and ISO9660 (Unicode UDF)", null, null, null, null, true);

        //[Theory]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", @"ri:.*")]
        [InlineData("Scan", "")]
        public void Ps3_Grxxra(string task, string taskOptions) =>
            createTests("Ps3", task, taskOptions, "Grxxra Uloevq (HFN) (Ra,Wn,Se,Qr,Rf,Vg,Xb).7z", "Retail     / ISO       / 37.9GiB / UDF NSR03, Hybrid bd-video + game", null, _Keys, _FixPs3, null, true);

        //[Theory]
        [InlineData("Convert", "cso")]
        [InlineData("Convert", "cso2")]
        [InlineData("Convert", "deciso")]
        [InlineData("Convert", "zso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Ps3_JrQner(string task, string taskOptions) =>
            createTests("Ps3", task, taskOptions, "Jr Qner - Syvegl Sha sbe Nyy (Rhebcr, Nhfgenyvn) (Ra,Se,Qr,Rf,Vg).7z", "Retail     / ISO       / 367MiB  / Regular, small Image", null, _Keys, _FixPs3, null, true);

        //[Theory]
        [InlineData("Convert", "deciso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Ps3_Qvfarl(string task, string taskOptions) =>
            createTests("Ps3", task, taskOptions, "Qvfarl Cuvarnf naq Sreo - Npebff gur 7aq Qvzrafvba (Rhebcr).7z", "Retail     / ISO       / 17.5GiB / Split interleaved files", null, _Keys, _FixPs3, null, true);

        //[Theory]
        [InlineData("Convert", "zso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Ps3_QzP(string task, string taskOptions) =>
            createTests("Ps3", task, taskOptions, "QzP - Qrivy Znl Pel (Rhebcr) (Ra,Se,Qr,Rf,Vg,Ay,Cg,Cy,Eh) (Orgn).7z", "Beta       / ISO       / 6.75GiB / Non retail disc identifier", null, _Keys, _FixPs3, null, true);

        //[Theory]
        [InlineData("Convert", "cso")]
        [InlineData("Convert", "cso2")]
        [InlineData("Convert", "deciso")]
        [InlineData("Convert", "zso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Ps3_Zvarpensg(string task, string taskOptions) =>
            createTests("Ps3", task, taskOptions, "Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN).rar", "Retail     / ISO       / 367MiB  / Regular, small Image", null, _Keys, _FixPs3, null, true);

        //[Theory]
        [InlineData("Fix", "")]
        public void Ps3_Zvarpensg_ex(string task, string taskOptions) =>
            createTests("Ps3", task, taskOptions, "Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted.7z", "Retail     / ISO       / 367MiB  / Regular, small Image", null, _Keys, _FixPs3, _FixFilesPs3, true);

        //[Theory]
        [InlineData("Convert", "cso")]
        [InlineData("Convert", "cso2")]
        [InlineData("Convert", "zso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Psp_552Sebs(string task, string taskOptions) =>
            createTests("Psp", task, taskOptions, "552 - Sebz Ehffvn jvgu Ybir (HX) (Ra,Se,Qr,Rf,Vg).dax", "Retail     / ISO       / 755MiB  / Dax 4096 - Regular Iso", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "zso")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void Psp_552Sebs2(string task, string taskOptions) =>
            createTests("Psp", task, taskOptions, "552 - Sebz Ehffvn jvgu Ybir (HX) (Ra,Se,Qr,Rf,Vg).jso", "Retail     / ISO       / 755MiB  / Jiso lzo 2048 - Regular Iso", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:y")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:y")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_Eiy(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9).7z", "RVT-H      / gcm (7z)  / 4.38GiB / RVT-H Manually Scrubbed", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:n")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:n")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_Fhcre(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "Fhcre Fznfu Oebf. Oenjy (Rhebcr) (Ra,Se,Qr,Rf,Vg).rvz", "Retail     / RVZ       / 8GiB    / Retail with Virtual Console partitions", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:y")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:y")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_FhcreZnevb(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "Fhcre Znevb Nyy-Fgnef (Rhebcr)_MFGQ673.rvz", "Retail     / RVZ       / 4.38GiB / Normal", null, null, null, null, true);

        //[Theory]
        [InlineData("Scan", "")]
        public void Wii_FreeLoaderE(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "FreeLoader for Nintendo Wii (Europe) (Unl).nkit.gcz", "Unlicenced / NKit.Gcz  / 1.36GiB / Hacked", null, null, null, null, true);

        //[Theory]
        [InlineData("Scan", "")]
        public void Wii_FreeLoaderJ(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "FreeLoader for Nintendo Wii (Japan) (Unl).nkit.gcz", "Unlicenced / NKit.Gcz  / 1.36GiB / Hacked", null, null, null, null, true);

        //[Theory]
        [InlineData("Scan", "")]
        public void Wii_FreeLoaderU(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "FreeLoader for Nintendo Wii (USA) (Unl).nkit.iso", "Unlicenced / NKit.Iso  / 1.36GiB / Hacked", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:y")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:y")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_JvvOnpxhcQvfp(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "Jvv OnpxhcQvfp [565R56].rvz", "Retail     / RVZ       / 600MiB  / Truncated, Bad Scrubbing, Update Partition is 1 sections", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:n")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:n")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_JvvSvg(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "Jvv Svg Cyhf (Xbern).rvz", "Retail     / RVZ       / 4.38GiB / Channel between Update and Data", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:y")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:y")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_JvvZrah(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "Jvv Zrah Vafgnyy Qvfp (Ire. 6) (Jbeyq).rvz", "RVT-R      / RVZ       / 4.38GiB / Update Partition is incremental Ints, gap larger than 0xffffffff", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:n")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:n")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_Mhzon(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "Mhzon Svgarff - Jbeyq Cnegl (Rhebcr) (Ra,Se,Qr,Rf,Vg).rvz", "Retail     / RVZ       / 8GiB    / Dual layer, < 4GiB with update removed", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:y")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:y")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_Qentba(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7).rvz", "Retail     / RVZ       / 4.38GiB / Win Partition, Disc 2", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:n")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:n")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_QvfarlVasvavgl(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "Qvfarl Vasvavgl (Rhebcr) (Ra,Se,Qr,Rf,Vg,Ay).rvz", "Retail     / RVZ       / 8GiB    / No trailing nulls at start of block for files ending on group end boundary (FST claims theres a file on scrubbed block - unscrubs fine)", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:y")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:y")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_Tevz(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "Tevz Nqiragherf bs Ovyyl & Znaql, Gur (Rhebcr).rvz", "Retail     / RVZ       / 4.38GiB / Weird Data", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:n")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:n")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_UneirfgZbba(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "Uneirfg Zbba - Gerr bs Genadhvyvgl (HFN).rvz", "Retail     / RVZ       / 4.38GiB / Fst spans section, 0 byte files", null, null, null, null, true);

        //[Theory]
        [InlineData("Convert", "ciso:y")]
        [InlineData("Convert", "rvz")]
        [InlineData("Convert", "wbfs:y")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("FixExtract", "")]
        [InlineData("Scan", "")]
        public void Wii_Znevb(string task, string taskOptions) =>
            createTests("Wii", task, taskOptions, "Znevb Xneg Jvv (HFN) (Ra,Se,Rf).rvz", "Retail     / RVZ       / 4.38GiB / Channel after game partition", null, null, null, null, true);

        //[Theory]
        [InlineData("Fix", "")]
        public void Wii_ZnevbFix(string task, string taskOptions) => //This image is game only. Fix will unscrub with invalid H3 data (as it's wiped). So the dat must contain checksums of wiped update, unscrubbed game and wiped channel
            createTests("Wii", task, taskOptions, "Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly.zip", "Retail     / ISO       / 4.38GiB / Wiped WBFS image containing only the Game partitions (No update or channel)", _DatWii, null, null, _FixFilesWii, true);

        //[Theory]
        [InlineData("Convert", "app")]
        [InlineData("Convert", "wux")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void WiiU_JvvHPng(string task, string taskOptions) =>
            createTests("WiiU", task, taskOptions, "JvvH_PNG-V.wux", "CAT-I      / WUX       / 23.4GiB / Decrypted SI partition, CommonDev key for game ticket", null, _Keys, null, null, true);

        //[Theory]
        [InlineData("Convert", "app")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void WiiU_JvvHPqa(string task, string taskOptions) =>
            createTests("WiiU", task, taskOptions, "JvvH_Pqa_SMrebK_HFN.7z", "Retail     / APP       / 60MiB   / CDN tmd, no tik, no cert, no h3 files", null, _Keys, null, null, true);

        //[Theory]
        [InlineData("Convert", "app")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void WiiU_JvvHPqaZpc(string task, string taskOptions) =>
            createTests("WiiU", task, taskOptions, "JvvH_Pqa_Zpc_GvgyrVQ_Yvfg.7z", "Update     / APP       / 284KiB  / tmd.6 Update tmd+cetk files. Multi versions in archive", null, _Keys, null, null, true);

        //[Theory]
        [InlineData("Convert", "app")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void WiiU_Pbpbgb(string task, string taskOptions) =>
            createTests("WiiU", task, taskOptions, "Pbpbgb Zntvp Pvephf 7 (Rhebcr) (Ra,Se,Qr,Rf,Vg,Ay,Cg) [555055556567Q555].7z", "Retail     / APP       / 367MiB  / App with tmd, tik, cert and H3 files", null, _Keys, null, null, true);

        //[Theory]
        [InlineData("Convert", "app")]
        [InlineData("Convert", "wux")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void WiiU_PbpbgbZntvp(string task, string taskOptions) =>
            createTests("WiiU", task, taskOptions, "Pbpbgb Zntvp Pvephf 7 (Rhebcr) (Ra,Se,Qr,Rf,Vg,Ay,Cg).wux", "Retail     / WUX       / 23.4GiB / Game Partition", null, _Keys, null, null, true);

        //[Theory]
        [InlineData("Convert", "app")]
        [InlineData("Convert", "wux")]
        [InlineData("Expand", "")]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void WiiU_Yrtraq(string task, string taskOptions) =>
            createTests("WiiU", task, taskOptions, "Yrtraq bs Mryqn, Gur - Oerngu bs gur Jvyq (HFN) (Ra,Se,Rf).nkds", "Retail     / WUX       / 23.4GiB / 2 Game Partitions, GI Partition", null, null, null, null, true);

        //[Theory]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void XBox_Frtn(string task, string taskOptions) =>
            createTests("XBox", task, taskOptions, "Frtn TG 7557 + WFES - Wrg Frg Enqvb Shgher (HFN) (Ra,Wn).chd", "Retail     / ISO       / 7.29GiB / Many files, 3 block FST", null, null, null, null, true);

        //[Theory]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void XBox_RFCA(string task, string taskOptions) =>
            createTests("XBox", task, taskOptions, "RFCA Pbyyrtr Ubbcf 7X0 (HFN).chd", "Retail     / ISO       / 7.29GiB / Root FST is Before Volume Header", null, null, null, null, true);

        //[Theory]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void XBox_Cebwrpg(string task, string taskOptions) =>
            createTests("XBox360", task, taskOptions, "Cebwrpg Flycurrq (Jbeyq) (Orgn) (7552-59-50).chd", "Retail     / ISO       / 6.36GiB / Default ISO", null, null, null, null, true);

        //[Theory]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void XBox_Obeqreynaqf(string task, string taskOptions) =>
            createTests("XBox360", task, taskOptions, "Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr).chd", "Retail     / ISO       / 7.20GiB / Data in Video Part 2, extra pvd in xdvdfs", null, null, null, null, true);

        //[Theory]
        [InlineData("Extract", "f")]
        [InlineData("Extract", "ri:.*")]
        [InlineData("Scan", "")]
        public void XBox_Qrnq(string task, string taskOptions) =>
            createTests("XBox360", task, taskOptions, "Qrnq be Nyvir Kgerzr 7 (HFN) (Ra,Wn,Se,Qr,Rf,Vg,Mu,Xb,Cy).chd", "Retail     / ISO       / 7.30GiB / Regular Retail", null, null, null, null, true);

        private void createTests(string sys, string task, string taskOptions, string fileName, string info, string dats, string keys, string fixInfo, string fixFiles, bool commentTests)
        {
            string opt = Regex.Replace(taskOptions, "[^a-zA-Z0-9]", "");
            if (opt.Length != 0)
                opt = opt[0].ToString().ToUpper() + (opt.Length < 2 ? "" : opt.Substring(1));
            if (task == "Extract" && taskOptions == "f")
                opt = "Forensic";
            else if (task == "Extract" && taskOptions == "ri:.*")
                opt = "";
            else if (task == "Extract" && taskOptions == "mi:*")
                opt = "Mask";

            string testName = $"{task}{opt}";
            string name = Regex.Replace(fileName, "[^a-z0-9_]+", "_", RegexOptions.IgnoreCase).Trim('_');
            string inPath = Path.GetFullPath(Path.Combine(_InPath, sys));
            string outFolderName = $"{sys}_{name}_{testName}_{Guid.NewGuid():N}";

            SystemPresetSettings presets = CreatePresets(task, taskOptions, inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = Enum.Parse<SystemType>(sys, true);

            switch (presets.Task)
            {
                case TaskType.Convert:
                    presets.Convert = taskOptions;
                    break;
                case TaskType.Extract:
                    presets.Extract = taskOptions;
                    break;
            }

            NKitTaskResults t = base.ProcessImage(presets);

            testName = testName.Replace("Create", "").Replace("Tests", "");
            string className = $"{name}_{testName}_Tests";
            string path = Path.Combine(base.GetPath(), sys);
            string extractFileName = null;
            Directory.CreateDirectory(path);

            if (presets.Task == TaskType.Extract)
            {
                StringBuilder files = new StringBuilder(0x1000); //4k
                foreach (ExtractFileTestItem f in base.ExtractFileResults)
                    files.AppendLine($"{f.Crc:x8}\t{f.Size:x}\t/{f.FileName.Replace('\\', '/')}");
                foreach (string d in base.ExtractFileDirectories)
                    files.AppendLine(d);
                extractFileName = Path.Combine(path, className + ".txt");
                File.WriteAllText(extractFileName, files.ToString());
            }
            File.WriteAllText(Path.Combine(path, className + ".cs"), WipedImageTestsTemplate.CreateTest(t, testName, taskOptions, sys, _InPath, Path.GetFullPath(outFolderName), fileName, name, info, extractFileName, dats, keys, fixInfo, fixFiles)); //Add Source datitems, add index file, paths for convert OUT

            if (commentTests)
            {
                //revert the //Theory attribute and apply the comment
                string thisFn = GetThisFileName();
                string content = File.ReadAllText(thisFn);
                File.WriteAllText(thisFn, Regex.Replace(content, @"([ \t]+)(\[Theory\])", @"$1//$2"));
            }

            base.Complete(); //delete the directory
        }

        internal string GetThisFileName([CallerFilePath] string fullFileName = null) => fullFileName;

        //[Fact]
        public void ManualWipedScanFileSizeTest()
        {
            string[] files =
@"".Replace("\r", "").Split('\n');

            List<Tuple<long, string>> filesA = new List<Tuple<long, string>>();
            List<Tuple<long, string>> filesB = new List<Tuple<long, string>>();

            Regex isFile = new Regex(@"^[ \t]*<File .* FullSize=""([0-9A-F]+)"" .* File=""(.*?)""", RegexOptions.Compiled);

            foreach (string l in File.ReadLines(files[0]))
            {
                Match m = isFile.Match(l);
                if (m.Success)
                    filesA.Add(new Tuple<long, string>(long.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber), m.Groups[2].Value));
            }

            foreach (string l in File.ReadLines(files[1]))
            {
                Match m = isFile.Match(l);
                if (m.Success)
                    filesB.Add(new Tuple<long, string>(long.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber), m.Groups[2].Value));
            }

            if (filesA.Count != filesB.Count)
                throw new Exception("Diff Counts");

            if (filesA.Count == 0)
                throw new Exception("No Matches");

            for (int i = 0; i < filesA.Count; i++)
            {
                if (filesA[i].Item1 != filesB[i].Item1)
                    throw new Exception("Size Diffs");

            }

            throw new Exception("Disable this test");
        }
    }
}