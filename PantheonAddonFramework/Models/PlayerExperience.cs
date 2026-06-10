namespace PantheonAddonFramework.Models;

public class PlayerExperience
{
    public double Current { get; }
    public double ToNextLevel { get; }
    public float ExperiencePercentage { get; }

    public PlayerExperience(double current, double toNextLevel, float experiencePercentage)
    {
        Current = current;
        ToNextLevel = toNextLevel;
        ExperiencePercentage = experiencePercentage;
    }
}