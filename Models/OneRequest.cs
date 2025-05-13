namespace MyTts.Models
{
    /// <summary>
    /// Request model for the "one" endpoint.
    /// </summary>
    public class OneRequest
    {
        public required int News { get; set; }
        public required string Language { get; set; } = "tr";
    }
}