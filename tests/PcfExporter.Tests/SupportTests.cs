using PcfExporter.Core;
using Xunit;
namespace PcfExporter.Tests;
public class SupportTests
{
 static ModelElement Pipe(string id) => new(){Id=id,Family="Pipe",Ports=[new(new(0,0,0),50,new(-1,0,0)),new(new(100,0,0),50,new(1,0,0))]};
 static ModelElement Support(string id="S",string? host="P",string[]? neighbors=null,Point? point=null,double? bore=50)=>new(){Id=id,Family="Hanger",IsSupport=true,Location=point??new(20,0,0),HostId=host,HostBore=bore,Neighbors=(neighbors??[]).ToHashSet()};
 static Configuration Config()=>new(){Groups=[new(){Name="Pipes",Families=["Pipe"],Mappings=new(){["ComponentType"]=new(){Constant="PIPE"},["Description"]=new(){Constant="Pipe"}}},new(){Name="Supports",Families=["Hanger"],Mappings=new(){["ComponentType"]=new(){Constant="SUPPORT"},["Description"]=new(){Constant="Pipe guide"},["SupportName"]=new(){Constant="GUIDE-01"},["Skey"]=new(){Constant="GUID"}}}]};
 [Fact]public void HostAttachesSupportToPipeNetwork(){var n=NetworkDiscovery.Discover([Pipe("P"),Support()]);Assert.Single(n);Assert.Equal(2,n[0].Elements.Count);}
 [Fact]public void ConnectorlessSupportWritesInsertionPointBoreAndName(){var text=PcfWriter.Compose(NetworkDiscovery.Discover([Pipe("P"),Support()]),Config());Assert.Contains("SUPPORT\r\n    CO-ORDS 20 0 0 50\r\n    NAME GUIDE-01\r\n    SKEY GUID",text);Assert.Contains("DESCRIPTION Pipe guide",text);}
 [Fact]public void SupportDoesNotBridgeDisconnectedRuns(){Assert.Throws<MappingException>(()=>NetworkDiscovery.Discover([Pipe("P"),Pipe("Q"),Support(neighbors:["P","Q"])]));}
 [Fact]public void UnattachedSupportIsExplicitStandaloneNetwork(){var n=NetworkDiscovery.Discover([Pipe("P"),Support(host:null)]);Assert.Equal(2,n.Count);Assert.Contains(n,x=>x.Label.Contains("Unattached support"));}
 [Fact]public void SelectionDoesNotPullInUnselectedHost(){var n=NetworkDiscovery.Discover([Support()]);Assert.Single(n);Assert.Single(n[0].Elements);}
 [Fact]public void SeparateOutputKeepsSupportWithItsHost(){var files=PcfWriter.Plan(NetworkDiscovery.Discover([Pipe("P"),Pipe("Q"),Support()]),Config(),true);Assert.Contains("SUPPORT\r\n",files["network-P.pcf"]);Assert.DoesNotContain("SUPPORT\r\n",files["network-Q.pcf"]);}
 [Fact]public void MissingBoreBlocksExport(){Assert.Throws<MappingException>(()=>PcfWriter.Compose(NetworkDiscovery.Discover([Support(bore:null)]),Config()));}
 [Fact]public void ExplicitBoreMappingSupportsNonHostedFamilies(){var c=Config();c.Groups[1].Mappings["SupportBore"]=new(){Constant="80"};Assert.Contains("CO-ORDS 20 0 0 80",PcfWriter.Compose(NetworkDiscovery.Discover([Support(bore:null)]),c));}
 [Fact]public void ComponentValueTranslationClassifiesSupport(){var c=Config();c.Groups[1].Mappings["ComponentType"]=new(){Source="Instance",Parameter="Role",Translations=new(){{"Guide","SUPPORT"}}};Assert.True(MappingResolver.IsSupport(new(){Family="Hanger",Instance=new(){{"Role","Guide"}}},c));}
 [Fact]public void MissingLocationBlocksExport(){var e=new ModelElement{Id="S",Family="Hanger",IsSupport=true,HostBore=50};Assert.Throws<MappingException>(()=>PcfWriter.Compose(NetworkDiscovery.Discover([e]),Config()));}
}
