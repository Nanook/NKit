namespace NkdsUi.Models;

[Flags]
public enum ScopeRestriction
{
    None = 0,
    SameSet = 1,
    SameSystem = 2,
    Both = SameSet | SameSystem
}