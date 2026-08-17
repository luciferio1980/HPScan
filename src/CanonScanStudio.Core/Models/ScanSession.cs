namespace CanonScanStudio.Models;

public sealed class ScanSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Documento";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;
    public List<ScanPage> Pages { get; set; } = [];
    public bool IsDirty { get; set; }

    public void Renumber()
    {
        for (var i = 0; i < Pages.Count; i++)
        {
            Pages[i].Order = i;
        }
    }

    public ScanPage? GetPage(Guid id) => Pages.FirstOrDefault(p => p.Id == id);

    public void MovePage(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Pages.Count || toIndex < 0 || toIndex >= Pages.Count || fromIndex == toIndex)
        {
            return;
        }

        var page = Pages[fromIndex];
        Pages.RemoveAt(fromIndex);
        Pages.Insert(toIndex, page);
        Renumber();
        IsDirty = true;
        ModifiedAt = DateTimeOffset.Now;
    }

    public void ApplyOrder(IReadOnlyList<Guid> orderedIds)
    {
        if (orderedIds.Count == 0 || Pages.Count == 0)
        {
            return;
        }

        var remaining = Pages.ToDictionary(p => p.Id);
        var next = new List<ScanPage>(Pages.Count);
        foreach (var id in orderedIds)
        {
            if (remaining.Remove(id, out var page))
            {
                next.Add(page);
            }
        }

        next.AddRange(remaining.Values);
        Pages.Clear();
        Pages.AddRange(next);
        Renumber();
        IsDirty = true;
        ModifiedAt = DateTimeOffset.Now;
    }
}
