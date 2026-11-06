int count=0;
RenderingServiceChecks.Run((passed,message)=>{count++;if(!passed)throw new Exception(message);});
MovementTerrainChecks.Run((passed,message)=>{count++;if(!passed)throw new Exception(message);});
Console.WriteLine(count+" rendering service checks passed");
