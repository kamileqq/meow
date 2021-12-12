using System.Xml.Serialization;

namespace LBPUnion.ProjectLighthouse.Types.Levels
{
    public enum LevelType
    {
        [XmlEnum("normal")]
        Normal = 0,

        [XmlEnum("versus")]
        Versus = 1,

        [XmlEnum("cutscene")]
        Cutscene = 2,
    }
}