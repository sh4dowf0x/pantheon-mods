namespace PantheonAddonFramework;

[AttributeUsage(AttributeTargets.Class)]
public class AddonMetadataAttribute : Attribute
{
    public AddonMetadataAttribute(string name, string author, string description)
    {
        Name = name;
        Author = author;
        Description = description;
    }

    public string Name { get; }
    public string Author { get; }
    public string Description { get; }
}