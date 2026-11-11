namespace SkillHorizon.Learning;

public interface IEvidenceFiles
{
    Task<string> PutAsync(byte[] content, CancellationToken cancellationToken);

    Task<byte[]?> ReadAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed class FileEvidenceStore : IEvidenceFiles
{
    private readonly string _root;

    public FileEvidenceStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> PutAsync(byte[] content, CancellationToken cancellationToken)
    {
        var key = Guid.NewGuid().ToString("N");
        var path = Path.Combine(_root, key);
        await File.WriteAllBytesAsync(path, content, cancellationToken).ConfigureAwait(false);
        return key;
    }

    public async Task<byte[]?> ReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, objectKey);
        if (!File.Exists(path))
            return null;
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
