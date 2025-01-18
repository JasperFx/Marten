using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using JasperFx.Core;
using Microsoft.FSharp.Core;

#nullable enable
namespace Marten.Testing.Documents;

public enum Colors
{
    Red,
    Blue,
    Green,
    Purple,
    Yellow,
    Orange
}

public record Target
{
    private static readonly Random _random = new Random(67);

    private static readonly string[] _strings =
    {
        "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Violet", "Pink", "Gray", "Black"
    };

    private static readonly string[] _otherStrings =
    {
        "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"
    };

    public static IEnumerable<Target> GenerateRandomData(int number, bool includeFSharpUnionTypes = false)
    {
        var i = 0;
        while (i < number)
        {
            yield return Random(true, includeFSharpUnionTypes);

            i++;
        }
    }

    public static Target Random(bool deep = false, bool includeFSharpUnionTypes = false)
    {
        var target = new Target();
        target.String = _strings[_random.Next(0, 10)];

        if (includeFSharpUnionTypes)
        {
            target.FSharpGuidOption = new FSharpOption<Guid>(Guid.NewGuid());
            target.FSharpIntOption = new FSharpOption<int>(_random.Next(0, 10));
            target.FSharpDateOption = new FSharpOption<DateTime>(DateTime.Now);
            target.FSharpDateTimeOffsetOption = new FSharpOption<DateTimeOffset>(new DateTimeOffset(DateTime.UtcNow));
            target.FSharpDecimalOption = new FSharpOption<decimal>(_random.Next(0, 10));
            target.FSharpLongOption = new FSharpOption<long>(_random.Next(0, 10));
            target.FSharpStringOption = new FSharpOption<string>(_strings[_random.Next(0, 10)]);
        }

        target.PaddedString = " " + target.String + " ";
        target.AnotherString = _otherStrings[_random.Next(0, 10)];
        target.Number = _random.Next();
        target.AnotherNumber = _random.Next();
        target.OtherGuid = Guid.NewGuid();

        target.Flag = _random.Next(0, 10) > 5;

        target.Float = float.Parse(_random.NextDouble().ToString());

        target.NumberArray = _random.Next(0, 10) > 8
            ? new[] { _random.Next(0, 10), _random.Next(0, 10), _random.Next(0, 10) }
            : Array.Empty<int>();

        target.NumberArray = target.NumberArray.Distinct().ToArray();

        switch (_random.Next(0, 2))
        {
            case 0:
                target.Color = Colors.Blue;
                break;

            case 1:
                target.Color = Colors.Green;
                break;

            default:
                target.Color = Colors.Red;
                break;
        }

        var value = _random.Next(0, 100);
        if (value > 10) target.NullableNumber = value;

        if (value > 20)
        {
            var list = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                list.Add(_strings[_random.Next(0, 10)]);
            }

            target.StringArray = list.Distinct().ToArray();
        }

        target.Long = 100 * _random.Next();
        target.Double = _random.NextDouble();
        target.Long = _random.Next() * 10000;

        target.HowLong = TimeSpan.FromSeconds(target.Long);

        target.Date = DateTime.Today.AddDays(_random.Next(-10000, 10000));
        target.DateOffset = new DateTimeOffset(DateTime.Today.AddDays(_random.Next(-10000, 10000)));

        if (value > 15)
        {
            target.NullableDateOffset = DateTimeOffset.Now.Subtract(_random.Next(-60, 60).Seconds());
        }

        if (deep)
        {
            target.Inner = Random();

            var number = _random.Next(1, 10);
            target.Children = new Target[number];
            for (var i = 0; i < number; i++)
            {
                target.Children[i] = Random();
            }

            target.StringDict = Enumerable.Range(0, _random.Next(0, 10)).ToDictionary(i => $"key{i}", i => $"value{i}");
            target.String = _strings[_random.Next(0, 10)];
            target.OtherGuid = Guid.NewGuid();
        }

        return target;
    }

    public string PaddedString { get; set; }

    public Target()
    {
        Id = Guid.NewGuid();
        StringDict = new Dictionary<string, string>();
        StringList = new List<string>();
        GuidDict = new Dictionary<Guid, Guid>();
    }

    public Guid Id { get; set; }

    public int Number { get; set; }

    public int AnotherNumber { get; set; }

    public long Long { get; set; }
    public string String { get; set; }

    public FSharpOption<Guid> FSharpGuidOption { get; set; }
    public FSharpOption<int> FSharpIntOption { get; set; }
    public FSharpOption<bool> FSharpBoolOption { get; set; }
    public FSharpOption<long> FSharpLongOption { get; set; }
    public FSharpOption<decimal> FSharpDecimalOption { get; set; }
    public FSharpOption<string> FSharpStringOption { get; set; }
    public FSharpOption<DateTime> FSharpDateOption { get; set; }
    public FSharpOption<DateTimeOffset> FSharpDateTimeOffsetOption { get; set; }

    public string AnotherString { get; set; }

    public string[] StringArray { get; set; }

    public Guid OtherGuid { get; set; }

    public Target Inner { get; set; }

    public Colors Color { get; set; }

    public Colors? NullableEnum { get; set; }

    public bool Flag { get; set; }

    [JsonInclude] // this is needed to make System.Text.Json happy
    public string StringField;

    public double Double { get; set; }
    public decimal Decimal { get; set; }
    public DateTime Date { get; set; }
    public DateTimeOffset DateOffset { get; set; }
    public DateTimeOffset? NullableDateOffset { get; set; }

    [JsonInclude] // this is needed to make System.Text.Json happy
    public float Float;

    public int[] NumberArray { get; set; }

    public string[] TagsArray { get; set; }

    public HashSet<string> TagsHashSet { get; set; }

    public Target[] Children { get; set; }

    public int? NullableNumber { get; set; }
    public DateTime? NullableDateTime { get; set; }
    public bool? NullableBoolean { get; set; }
    public Colors? NullableColor { get; set; }

    public string? NullableString { get; set; }

    public IDictionary<string, string> StringDict { get; set; }
    public Dictionary<Guid, Guid> GuidDict { get; set; }

    public Guid UserId { get; set; }

    public List<string> StringList { get; set; }

    public Guid[] GuidArray { get; set; }

    public TimeSpan HowLong { get; set; }
}

public class FSharpTarget: Target
{

}

public class Address
{
    public Address()
    {
    }

    public Address(string text)
    {
        var parts = text.ToDelimitedArray();
        Address1 = parts[0];
        City = parts[1];
        StateOrProvince = parts[2];
    }

    public string Address1 { get; set; }
    public string Address2 { get; set; }
    public string City { get; set; }
    public string StateOrProvince { get; set; }
    public string Country { get; set; }
    public string PostalCode { get; set; }

    public bool Primary { get; set; }
    public string Street { get; set; }
    public string HouseNumber { get; set; }
}

public class Squad
{
    public string Id { get; set; }
}

public class BasketballTeam: Squad
{
}

public class FootballTeam: Squad
{
}

public class BaseballTeam: Squad
{
}
