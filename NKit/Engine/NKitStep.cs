using System;
using System.Collections.Generic;

namespace Nanook.NKit
{

    internal class NKitStep
    {
        private NKitInput _input;
        private bool _complete;
        private IImage _image;
        private TheOverseer _theOverseer;
        private IBufferPreProcessor _preProcessor;
        private NKitStepContext _stepContext;
        private DateTime _started;
        private Action<EngineStats> _statsCallback;

        public NKitStep(NKitInput input, NKitStepContext context, Action<EngineStats> statsCallback = null)
        {
            _stepContext = context;
            _started = DateTime.Now;
            _statsCallback = statsCallback;
            _theOverseer = new TheOverseer(context.ImageInfo, context);
            _input = input; //reads source format and marks missing/removed blocks of data
            _theOverseer.SetInput(_input);
            _image = _input.Image;
            _theOverseer.SetImage(_image);
        }

        public SystemType SystemType => _image.SystemType;

        // Read all sections through the NKitCore StreamBlockCore pipeline. The runner's ordered
        // completer does the post-processing tail (PostProcess/Scan/Overseer/Step.Process) and
        // yields each completed section in order.
        public IEnumerable<ISection> ReadSections()
        {
            _preProcessor = _image.GetPreProcessor();
            Engine.Core.NKitCoreRunner runner = new Engine.Core.NKitCoreRunner(
                _image, _input, _stepContext, _theOverseer, _preProcessor, _statsCallback);
            return runner.Run();
        }


        public IEnumerable<ISection> Patch()
        {
            DateTime dt = DateTime.Now;
            bool patched = false;
            foreach (ScanArea sra in _stepContext.Scan.Areas)
            {
                bool patchApplied = false;
                ISectionProcessor ns;

                for (int i = 0; i < sra.Sections.Count; i++)
                {
                    ScanSection s = sra.Sections[i];

                    ns = _image.PatchSection(s);

                    if (ns != null)
                    {
                        s.PatchApplied = patched = patchApplied = true;
                        _stepContext.Scan.RecalculatePatchedSectionSingleCrcs(s);
                        _stepContext.Step.Patched(ns);
                        yield return ns;
                    }
                }

                if (patchApplied)
                {
                    _stepContext.Scan.RecalculatePatchedSectionCrcs();
                    sra.CrcRecalculate();
                }
            }
            //if (patched)
            //    _context.Log.Detail(() => "Patching Time: " + (DateTime.Now - dt).ToString("hh\\:mm\\:ss\\.fff"));
        }

        // Cancellation on the NKitCore path is driven by the step context's CancelToken and
        // SkipType (checked by NKitCoreRunner / the read loop in NKitProcessor); this call is
        // retained for the caller's contract but no longer needs its own abort flag.
        internal void Abort() { }


        public void Complete()
        {
            if (!_complete)
            {
                _complete = true;
                _stepContext.Scan.RecalculateAreaCrcs();
                _image.SetScanProperties();
                _stepContext.Step.ProcessResults();
            }

            _preProcessor?.Complete();
            _preProcessor = null;
            return;
        }
    }
}