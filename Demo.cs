using System.Text.Json.Serialization;

public class JsonTest {
    [JsonPropertyName("@test.luke.com")]
    public string luke {get;set;}
}
