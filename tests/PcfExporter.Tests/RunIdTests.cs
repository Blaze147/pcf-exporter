using PcfExporter.Core;
using Xunit;
namespace PcfExporter.Tests;
public class RunIdTests
{
    static ModelElement E(string id, string? value, bool support = false, bool boundary = false) => new() { Id=id, IsSupport=support, Boundary=boundary, HostId=support?"1":null, Neighbors=["1","2","3"], Instance=value is null?new():new(){{"run_ID",value}}, Type=new(){{"run_ID","TYPE-A"}} };
    [Fact] public void ExactMatchExcludesMissingCaseAndWhitespaceDifferences() {
        var matches=RunIdScope.Filter([E("1","A"),E("2","a"),E("3"," A"),E("4","A "),E("5",null)],"run_ID","A");
        Assert.Equal("1",Assert.Single(matches).Id);
    }
    [Fact] public void PropertyNameIsExact() { Assert.Empty(RunIdScope.Filter([E("1","A")],"RUN_ID","A")); }
    [Fact] public void TypeParametersAreExplicitNotFallback() {
        Assert.Empty(RunIdScope.Filter([E("1",null)],"run_ID","TYPE-A"));
        Assert.Single(RunIdScope.Filter([E("1",null)],"run_ID","TYPE-A","Type"));
    }
    [Fact] public void SupportsMustMatchIndependentlyOfTheirHost() {
        var n=NetworkDiscovery.Discover(RunIdScope.Filter([E("1","A"),E("2","B",true),E("3","A",true)],"run_ID","A"));
        Assert.Equal(new[]{"1","3"},Assert.Single(n).Elements.Select(e=>e.Id));
    }
    [Fact] public void MatchingSupportDoesNotPullInNonmatchingHost() {
        var n=NetworkDiscovery.Discover(RunIdScope.Filter([E("1","B"),E("2","A",true)],"run_ID","A"));
        Assert.Equal("2",Assert.Single(Assert.Single(n).Elements).Id);
    }
    [Fact] public void BoundaryEquipmentIsNeverIncluded() {Assert.Empty(RunIdScope.Filter([E("1","A",boundary:true)],"run_ID","A"));}
    [Fact] public void EmptyValueOnlyMatchesAnExistingEmptyParameter() {Assert.Equal("1",Assert.Single(RunIdScope.Filter([E("1",""),E("2",null)],"run_ID","")).Id);}
    [Fact] public void MissingPropertyNameIsRejected() {Assert.Throws<ArgumentException>(()=>RunIdScope.Filter([]," ","A"));}
    [Fact] public void HistoryIsRecentFirstAndDeduplicated() {
        var c=new RunIdContext();c.Remember("run_ID","A","Instance");c.Remember("Line","B","Type");c.Remember("run_ID","C","Instance");
        Assert.Equal(new[]{"run_ID","Line"},c.ParameterHistory);Assert.Equal("C",c.LastValue);
    }
    [Fact] public void ContextPersistsAlongsideMappings() {
        var path=Path.GetTempFileName();File.Delete(path);
        try {var store=new ConfigurationStore(path);var c=new Configuration();c.RunId.Remember("run_ID","A","Type");store.Save(c);var loaded=store.Load();Assert.Equal("run_ID",Assert.Single(loaded.RunId.ParameterHistory));Assert.Equal("A",loaded.RunId.LastValue);Assert.Equal("Type",loaded.RunId.ParameterSource);}
        finally {File.Delete(path);File.Delete(path+".bak");}
    }
    [Fact] public void OldConfigurationWithoutRunContextStillLoads() {
        var path=Path.GetTempFileName();try{File.WriteAllText(path,"{\"Version\":1,\"Groups\":[]}");Assert.Empty(new ConfigurationStore(path).Load().RunId.ParameterHistory);}finally{File.Delete(path);}
    }
}
