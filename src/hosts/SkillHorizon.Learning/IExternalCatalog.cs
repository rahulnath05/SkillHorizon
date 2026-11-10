namespace SkillHorizon.Learning;

public interface IExternalCatalog
{
    int Calls { get; }

    Task AssignAsync(string subjectId, string title, CancellationToken cancellationToken);
}

public sealed class UnconnectedCatalog : IExternalCatalog
{
    public int Calls { get; private set; }

    public Task AssignAsync(string subjectId, string title, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.CompletedTask;
    }
}
