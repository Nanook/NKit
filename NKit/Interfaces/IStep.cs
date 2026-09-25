namespace Nanook.NKit
{
    internal interface IStep
    {
        void Initialise(IStepContext context);
        void Process(ISection section);
        void ProcessResults();
        void Patched(ISection section);
        string ProposedName(); //can be null if not accurate (can't calculate without data)
        void ProcessResultsAsExceptioned();
    }
}