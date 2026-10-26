using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace StudySpace.UnitTests;

public sealed class DesignSystemTests
{
    private static readonly string DirectoryPath=Path.Combine(AppContext.BaseDirectory,"Design");
    [Theory]
    [InlineData("Accent","OnPrimary")][InlineData("PrimaryDark","OnPrimary")]
    [InlineData("InkLight","SurfaceLight")][InlineData("MutedLight","SurfaceLight")]
    [InlineData("InkDark","SurfaceDark")][InlineData("MutedDark","SurfaceDark")]
    [InlineData("MutedLight","ElevatedLight")][InlineData("MutedDark","ElevatedDark")]
    [InlineData("DisabledInkLight","DisabledSurfaceLight")][InlineData("DisabledInkDark","DisabledSurfaceDark")]
    [InlineData("SuccessLight","SuccessSurfaceLight")][InlineData("SuccessDark","SuccessSurfaceDark")]
    [InlineData("ErrorLight","ErrorSurfaceLight")][InlineData("ErrorDark","ErrorSurfaceDark")]
    [InlineData("WarningLight","WarningSurfaceLight")][InlineData("WarningDark","WarningSurfaceDark")]
    public void Text_and_semantic_feedback_reach_four_point_five_contrast(string foreground,string background)
    {
        XNamespace x="http://schemas.microsoft.com/winfx/2009/xaml";
        var palette=XDocument.Load(Path.Combine(DirectoryPath,"Colors.xaml")).Root!.Elements().ToDictionary(e=>(string)e.Attribute(x+"Key")!,e=>e.Value.Trim());
        var a=Luminance(palette[foreground]);var b=Luminance(palette[background]);
        Assert.True((Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05)>=4.5,$"Contrast insufficient for {foreground}/{background}.");
    }
    [Fact] public void Pages_use_the_shared_palette_instead_of_literal_hex_colors()
    {
        var paths=Directory.GetFiles(Path.Combine(DirectoryPath,"Views"),"*.xaml",SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(DirectoryPath,"Controls"),"*.xaml"));
        Assert.True(paths.Count()>20);
        foreach(var path in paths)Assert.DoesNotMatch(@"#[0-9A-Fa-f]{3,8}\b",File.ReadAllText(path));
    }
    [Fact] public void Every_static_design_reference_exists_in_the_shared_dictionaries()
    {
        XNamespace x="http://schemas.microsoft.com/winfx/2009/xaml";
        var files=Directory.GetFiles(DirectoryPath,"*.xaml");
        var keys=files.SelectMany(p=>XDocument.Load(p).Descendants().Attributes(x+"Key").Select(a=>a.Value)).ToHashSet();
        Assert.Contains("PageInset",keys);Assert.Contains("QuestionMarker",keys);Assert.Contains("PrimaryButton",keys);
        foreach(var file in Directory.GetFiles(DirectoryPath,"*.xaml",SearchOption.AllDirectories))
            foreach(Match match in Regex.Matches(File.ReadAllText(file),@"\{StaticResource\s+([^}\s]+)\}"))
                Assert.Contains(match.Groups[1].Value,keys);
    }
    private static double Luminance(string hex)
    {
        var values=Enumerable.Range(0,3).Select(i=>int.Parse(hex.AsSpan(1+i*2,2),NumberStyles.HexNumber)/255d)
            .Select(v=>v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4)).ToArray();
        return values[0]*0.2126+values[1]*0.7152+values[2]*0.0722;
    }
}
