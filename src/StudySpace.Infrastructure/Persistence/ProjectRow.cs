using System.Globalization;
using StudySpace.Domain.Projects;
namespace StudySpace.Infrastructure.Persistence;

// SQLite reflection mapping is kept outside Domain.
public sealed class ProjectRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Color { get; set; } = "";
    public string IconKey { get; set; } = "";
    public bool IsFavorite { get; set; }
    public string CreatedAtUtc { get; set; } = "";
    public string UpdatedAtUtc { get; set; } = "";
    public int Revision { get; set; }
    public StudyProject ToDomain() => new()
    {
        Id = Guid.Parse(Id), Name = Name, Description = Description, Color = Color,
        IconKey = IconKey, IsFavorite = IsFavorite, Revision = Revision,
        CreatedAtUtc = DateTimeOffset.Parse(CreatedAtUtc, CultureInfo.InvariantCulture),
        UpdatedAtUtc = DateTimeOffset.Parse(UpdatedAtUtc, CultureInfo.InvariantCulture)
    };
}

