using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Test.Tests;

[TestClass]
public class JsonConvertersTest
{
    [JsonConverter(typeof(JsonStringEnumFallbackConverter<Fruit>))]
    [JsonEnumFallback(Unknown)]
    private enum Fruit
    {
        Unknown,
        Apple,
        Pear
    }

    [Flags]
    [JsonConverter(typeof(JsonStringEnumFallbackConverter<Toppings>))]
    [JsonEnumFallback(None)]
    private enum Toppings
    {
        None = 0,
        Nuts = 1,
        Cream = 2,
        Syrup = 4
    }

    // a fallback of the enum's type that is none of its values
    [JsonEnumFallback((Broken)99)]
    private enum Broken
    {
        One,
        Two
    }

    [TestMethod]
    [DataRow("1", Fruit.Apple)]
    [DataRow("\"pear\"", Fruit.Pear)]
    [DataRow("99", Fruit.Unknown)]
    [DataRow("\"Plum\"", Fruit.Unknown)]
    // beyond the enum's own type: cut down to 32 bits it would be 1, an apple
    [DataRow("4294967297", Fruit.Unknown)]
    [DataRow("-4294967295", Fruit.Unknown)]
    [DataRow("1.5", Fruit.Unknown)]
    public void An_enum_reads_what_it_has_and_falls_back_for_the_rest(string json, object expected)
    {
        Assert.AreEqual((Fruit)expected, JsonSerializer.Deserialize<Fruit>(json));
    }

    [TestMethod]
    [DataRow("\"Nuts, Syrup\"", Toppings.Nuts | Toppings.Syrup)]
    [DataRow("3", Toppings.Nuts | Toppings.Cream)]
    [DataRow("\"cream\"", Toppings.Cream)]
    [DataRow("0", Toppings.None)]
    // a name or a bit the enum does not have
    [DataRow("\"Nuts, Sprinkles\"", Toppings.None)]
    [DataRow("9", Toppings.None)]
    public void A_flags_enum_reads_a_combination_of_its_values(string json, object expected)
    {
        Assert.AreEqual((Toppings)expected, JsonSerializer.Deserialize<Toppings>(json));
    }

    [TestMethod]
    public void A_flags_enum_reads_back_what_it_wrote()
    {
        const Toppings toppings = Toppings.Cream | Toppings.Syrup;
        var json = JsonSerializer.Serialize(toppings);
        Assert.AreEqual("\"Cream, Syrup\"", json);
        Assert.AreEqual(toppings, JsonSerializer.Deserialize<Toppings>(json));
    }

    [TestMethod]
    public void A_fallback_that_is_none_of_the_enums_values_is_refused()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new JsonStringEnumFallbackConverter<Broken>());
    }

    // Among the settings' own converters, the array's serializer is this converter: one that passed the array back
    // to the settings never came back.
    [TestMethod]
    public void The_null_to_empty_array_converter_works_for_every_array_of_the_settings()
    {
        var options = new JsonSerializerOptions { Converters = { new NullToEmptyArrayConverter<string>() } };

        const string json = """{"Names":["a",null,"b"],"More":null}""";
        var lists = JsonSerializer.Deserialize<Lists>(json, options);
        Assert.IsNotNull(lists);
        CollectionAssert.AreEqual(new[] { "a", null, "b" }, lists.Names);
        Assert.IsEmpty(lists.More);
        Assert.AreEqual("""{"Names":["a",null,"b"],"More":[]}""", JsonSerializer.Serialize(lists, options));
        Assert.AreEqual("null", JsonSerializer.Serialize<string[]?>(null, options));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<string[]>("5", options));
    }

    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class Lists
    {
        public string?[] Names { get; init; } = [];
        public string?[] More { get; init; } = ["default"];
    }
}
