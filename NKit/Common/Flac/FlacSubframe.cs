namespace CUETools.Codecs.Flake
{
    unsafe public class FlacSubframe
    {
        public FlacSubframe()
        {
            rc = new RiceContext();
            coefs = new int[Lpc.MAX_LPC_ORDER];
        }
        internal SubframeType type;
        internal int order;
        internal int* residual;
        internal RiceContext rc;
        internal uint size;

        internal int cbits;
        internal int shift;
        internal int[] coefs;
        internal int window;
    };
}
