namespace EnterpriseAIPlatform.Infrastructure.ModelAccess;

/// <summary>Azure SQL connection settings for the admin/system config entities (spec 014, constitution Data &amp; Storage).</summary>
public sealed class ModelAccessSqlOptions
{
    public const string SectionName = "ModelAccessSql";

    public string? ConnectionString { get; init; }
}
