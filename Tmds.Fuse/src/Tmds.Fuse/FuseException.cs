namespace Tmds.Fuse
{
    public class FuseException : System.Exception
    {
        public FuseException() { }
        public FuseException(string message) : base(message) { }
        public FuseException(string message, System.Exception inner) : base(message, inner) { }
    }
}