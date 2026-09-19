using ZapretDesktop;
var client=new ServiceClient();
var state=await client.Send(new("status"));Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(state));
if(args.Contains("--manual")||args.Contains("--auto")){
 if(state.Running||state.Busy||state.RecoveryPending)throw new Exception("Existing connection present: not modified.");
 try{
  state=await client.Send(new("start",args.Contains("--auto"),"general (ALT)","general (ALT)"));
  for(int i=0;state.Busy&&i<240;i++){await Task.Delay(500);state=await client.Send(new("status"));}
  if(!state.Running||state.Error is not null)throw new Exception("Connection failed: "+state.Error);
  Console.WriteLine("PASS real requested mode starts");
 }finally{await client.StopAsync();}
 state=await client.Send(new("status"));
 if(state.Running||state.Busy||state.RecoveryPending)throw new Exception("Stop failed");
 Console.WriteLine("PASS real engine stopped");
}

