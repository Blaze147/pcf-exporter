using PcfExporter.Core;
using Xunit;
using System.Globalization;
namespace PcfExporter.Tests;
public class LogicTests {
 static Port Port(double x,double y=0, double dx=1,double dy=0)=>new(new(x,y,0),50,new(dx,dy,0));
 static ModelElement E(string id,string family="Pipe",string[]? neighbors=null,bool boundary=false,List<Port>? ports=null)=>new(){Id=id,Family=family,Neighbors=(neighbors??[]).ToHashSet(),Boundary=boundary,System="Supply",Ports=ports??[Port(0),Port(1000)],Instance=new(){{"Metal","CI"}},Type=new(){{"Label","Cast pipe"}}};
 static Configuration Config()=>new(){Groups=[new(){Name="Standard",Families=["Pipe"],Mappings=new(){["ComponentType"]=new(){Constant="PIPE"},["Description"]=new(){Source="Type",Parameter="Label"},["Material"]=new(){Source="Instance",Parameter="Metal",RequireTranslation=true,Translations=new(){{"CI","Cast iron"}}}}}]};
 [Fact]public void SameNamedDisconnectedRunsRemainSeparate(){var n=NetworkDiscovery.Discover([E("1",neighbors:["2"]),E("2"),E("3")]);Assert.Equal(2,n.Count);Assert.Equal(2,n[0].Elements.Count);}
 [Fact]public void EquipmentDoesNotBridgeSupplyAndReturn(){Assert.Equal(2,NetworkDiscovery.Discover([E("1",neighbors:["pump"]),E("pump",neighbors:["2"],boundary:true),E("2",neighbors:["pump"])]).Count);}
 [Fact]public void CyclesAndAsymmetricLinksAreHandled(){Assert.Single(NetworkDiscovery.Discover([E("1",neighbors:["2"]),E("2",neighbors:["3"]),E("3",neighbors:["1"])]));}
 [Fact]public void SelectionDoesNotExpandOutsideScope(){Assert.Single(NetworkDiscovery.Discover([E("1",neighbors:["2"])]).Single().Elements);}
 [Fact]public void InstanceTypeAndTranslationsResolve(){var r=MappingResolver.Resolve(E("1"),Config());Assert.Equal("Cast iron",r["Material"]);Assert.Equal("Cast pipe",r["Description"]);}
 [Fact]public void MissingMappingsBlockExport(){Assert.Throws<MappingException>(()=>MappingResolver.Resolve(E("1","Unknown"),Config()));}
 [Fact]public void UnknownEnumValueBlocksExport(){var c=Config();c.Groups[0].Mappings["Material"].Translations.Clear();Assert.Throws<MappingException>(()=>MappingResolver.Resolve(E("1"),c));}
 [Fact]public void DuplicateFamilyAssignmentIsRejected(){var c=Config();c.Groups.Add(new(){Families=["Pipe"]});Assert.Throws<InvalidDataException>(c.Validate);}
 [Fact]public void ConfigurationRoundTripsAndBacksUp(){string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try{var store=new ConfigurationStore(path);store.Save(Config());var c=store.Load();Assert.Equal("Cast iron",MappingResolver.Resolve(E("1"),c)["Material"]);store.Save(c);Assert.True(File.Exists(path+".bak"));}finally{File.Delete(path);File.Delete(path+".bak");}}
 [Fact]public void FutureSchemaIsRejected(){Assert.Throws<InvalidDataException>(()=>new Configuration{Version=2}.Validate());}
 [Fact]public void CombinedAndSeparatePlansPreserveNetworks(){var n=NetworkDiscovery.Discover([E("1"),E("2")]);Assert.Single(PcfWriter.Plan(n,Config(),false));Assert.Equal(2,PcfWriter.Plan(n,Config(),true).Count);var text=PcfWriter.Compose(n,Config());Assert.Equal(2,text.Split("PIPELINE-REFERENCE").Length-1);Assert.Contains("DESCRIPTION Cast pipe, Cast iron",text);Assert.Contains("END-POINT 1000 0 0 50",text);}
 [Fact]public void CultureDoesNotChangeCoordinates(){var old=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=new("de-DE");var e=E("1",ports:[Port(1.25),Port(1000)]);Assert.Contains("1.25 0 0 50",PcfWriter.Compose(NetworkDiscovery.Discover([e]),Config()));}finally{CultureInfo.CurrentCulture=old;}}
 [Fact]public void BadPortCountIsRejected(){Assert.Throws<MappingException>(()=>PcfWriter.Compose(NetworkDiscovery.Discover([E("1",ports:[Port(0)])]),Config()));}
 [Fact]public void UnknownComponentIsRejected(){var c=Config();c.Groups[0].Mappings["ComponentType"].Constant="MAGIC";Assert.Throws<MappingException>(()=>PcfWriter.Compose(NetworkDiscovery.Discover([E("1")]),c));}
 [Fact]public void NewlinesCannotInjectRecords(){var c=Config();c.Groups[0].Mappings["Description"]=new(){Constant="Bad\nPIPE"};Assert.Throws<MappingException>(()=>MappingResolver.Resolve(E("1"),c));}
 [Fact]public void ElbowAxesProduceCorner(){var c=Config();c.Groups[0].Mappings["ComponentType"].Constant="ELBOW";var e=E("1",ports:[Port(-100,0,1,0),Port(0,100,0,-1)]);Assert.Contains("CENTRE-POINT 0 0 0",PcfWriter.Compose(NetworkDiscovery.Discover([e]),c));}
 [Fact]public void EmptyScopeIsRejected(){Assert.Throws<MappingException>(()=>PcfWriter.Plan([],Config(),false));}
 [Fact]public void TeeFindsRunAndUnequalBranchIntersection(){var c=Config();c.Groups[0].Mappings["ComponentType"].Constant="TEE";var e=E("1",ports:[Port(0,75,0,1),Port(-100,0,-1,0),Port(200,0,1,0)]);var text=PcfWriter.Compose(NetworkDiscovery.Discover([e]),c);Assert.Contains("CENTRE-POINT 0 0 0",text);Assert.Contains("BRANCH1-POINT 0 75 0 50",text);}
 [Fact]public void CorruptJsonDoesNotBecomeDefaultConfiguration(){var path=Path.GetTempFileName();try{File.WriteAllText(path,"{invalid");Assert.Throws<System.Text.Json.JsonException>(()=>new ConfigurationStore(path).Load());}finally{File.Delete(path);}}
 [Fact]public void EccentricReducerRequiresOrientation(){var c=Config();c.Groups[0].Mappings["ComponentType"].Constant="REDUCER-ECCENTRIC";Assert.Throws<MappingException>(()=>PcfWriter.Compose(NetworkDiscovery.Discover([E("1")]),c));}
 [Fact]public void InvalidBoreIsRejected(){var e=E("1",ports:[new(new(0,0,0),double.NaN,new(1,0,0)),Port(100)]);Assert.Throws<MappingException>(()=>PcfWriter.Compose(NetworkDiscovery.Discover([e]),Config()));}
}
