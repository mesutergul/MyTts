namespace MyTts.Storage
{
    public class DiskConfiguration
    {
        public string Driver { get; set; } = "local";
        public string Root { get; set; } = string.Empty;
        public DiskConfig? Config { get; set; } = new();

    }
}
