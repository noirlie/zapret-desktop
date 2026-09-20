using ZapretDesktop;
class Program {
 static ProbeResult Yes=new(true,true,"HTTPS доступен","API доступен"),No=new(false,false,"blocked","blocked");
 static Strategy A=new("A","",[]),B=new("B","",[]),C=new("C","",[]);
 static int passed;
 static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);passed++;}
 static async Task Main(string[] args) {
  var engine=new FakeEngine();var probe=new FakeProbe(Yes,Yes,Yes);
  var result=await new AutoSelector(engine,probe).SelectAsync([A,B],null,true,_=>{},default);
  Check(!result.AlreadyAvailable&&engine.Starts.Count==1,"baseline HTTPS does not bypass engine startup");
  engine=new();probe=new(No,No,No,Yes,Yes);
  result=await new AutoSelector(engine,probe).SelectAsync([A,B,C],null,true,_=>{},default);
  Check(result.Strategy==B&&engine.Starts.SequenceEqual(new[]{"A","B"})&&engine.Running,"fallback and two-pass confirmation");
  engine=new();probe=new(Yes,Yes);
  result=await new AutoSelector(engine,probe).SelectAsync([A,B,C],"C",true,_=>{},default);
  Check(result.Strategy==C&&engine.Starts.Single()=="C","cached strategy first");
  engine=new();probe=new(No,Yes,No,Yes,Yes);
  result=await new AutoSelector(engine,probe).SelectAsync([A,B],null,true,_=>{},default);
  Check(result.Strategy==B,"single transient success rejected");
  engine=new();probe=new(No,No,No,No,No);
  try{await new AutoSelector(engine,probe).SelectAsync([A,B],null,true,_=>{},default);throw new Exception("expected no result");}catch(IOException){}
  Check(!engine.Running&&engine.Starts.Count==2,"all failures stop engine");
  engine=new();probe=new();
  result=await new AutoSelector(engine,probe).SelectAsync([A,B],null,false,_=>{},default);
  Check(engine.Starts.Count==1&&engine.Running&&!result.Verified,"manual mode keeps engine with unverified diagnosis");
  engine=new(){Fail="A"};probe=new(No,Yes,Yes);
  result=await new AutoSelector(engine,probe).SelectAsync([A,B],null,true,_=>{},default);
  Check(result.Strategy==B,"startup IO error skips candidate");
  engine=new(){Denied=true};probe=new(No);
  try{await new AutoSelector(engine,probe).SelectAsync([A,B],null,true,_=>{},default);throw new Exception("expected permission failure");}catch(UnauthorizedAccessException){}
  Check(engine.Starts.Count==1&&!engine.Running,"permission denial aborts rather than iterating");
  using var cancel=new CancellationTokenSource();engine=new();probe=new(No){BlockAfter=1};
  var task=new AutoSelector(engine,probe).SelectAsync([A,B],null,true,_=>{},cancel.Token);
  cancel.CancelAfter(50);
  try{await task;throw new Exception("cancel expected");}catch(OperationCanceledException){}
  Check(!engine.Running&&engine.Starts.Count==1,"cancellation stops active engine");
  using var cancelled=new CancellationTokenSource();cancelled.Cancel();engine=new();probe=new(No);
  try{await new AutoSelector(engine,probe).SelectAsync([A],null,true,_=>{},cancelled.Token);throw new Exception("cancel expected");}catch(OperationCanceledException){}
  Check(engine.Starts.Count==0,"pre-cancel prevents startup");
  using(var fastCancel=new CancellationTokenSource()){
   engine=new();probe=new(){BlockAfter=0};
   var pending=new AutoSelector(engine,probe).SelectAsync([A,B],"B",true,_=>{},fastCancel.Token);
   Check(engine.Running&&engine.Starts.SequenceEqual(new[]{"B"})&&!pending.IsCompleted,"cached engine starts before first network response");
   fastCancel.Cancel();try{await pending;}catch(OperationCanceledException){}
   Check(!engine.Running,"cancel during cached background verification stops engine");
  }
  var directory=Path.Combine(AppContext.BaseDirectory,"test-settings");Directory.CreateDirectory(directory);
  var store=new SettingsStore(directory);var settings=new UserSettings{Folder="test path",Automatic=false,SelectedStrategy="B"};settings.Working["hash"]="B";store.Save(settings);
  var restored=store.Load();Check(restored.Working["hash"]=="B"&&!restored.Automatic&&restored.Folder=="test path","settings atomic round trip");
  File.WriteAllText(Path.Combine(directory,"preferences.json"),"{broken");
  Check(store.Load().Automatic,"damaged settings recover defaults");
  Check(SettingsStore.NetworkKey().Length==64,"network identity hashed");
  engine=new();probe=new(new ProbeResult(false,true,"DNS: www.youtube.com","API"));
  try{await new AutoSelector(engine,probe).SelectAsync([A,B],null,true,_=>{},default);throw new Exception("Expected DNS failure");}catch(ProbeFailureException ex){Check(ex.Result.HasDnsFailure,"DNS failure preserves details");}
  Check(engine.Starts.Count==0,"DNS failure prevents pointless iteration");
  engine=new();probe=new();
  result=await new AutoSelector(engine,probe).SelectAsync([A],null,false,_=>{},default);
  Check(engine.Running&&!result.Verified,"manual startup allowed despite DNS test failure");
  using(var client=new System.Net.Http.HttpClient(new Handler(_=>throw new System.Net.Http.HttpRequestException(System.Net.Http.HttpRequestError.NameResolutionError,"dns")))){
   var answer=await WebProbe.Check(client,"https://www.youtube.com/",false,default);Check(!answer.ok&&answer.detail.StartsWith("DNS:"),"typed DNS exception classified");}
  using(var client=new System.Net.Http.HttpClient(new Handler(_=>throw new System.Net.Http.HttpRequestException(System.Net.Http.HttpRequestError.SecureConnectionError,"tls")))){
   var answer=await WebProbe.Check(client,"https://www.youtube.com/",false,default);Check(!answer.ok&&answer.detail.StartsWith("TLS:"),"TLS distinguished from DNS");}
  using(var client=new System.Net.Http.HttpClient(new Handler(_=>new(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.StreamContent(new StreamingPage())}))){
   var answer=await WebProbe.Check(client,"https://www.youtube.com/",false,default);Check(answer.ok,"HTML marker accepted without waiting for stream end");}
  using(var client=new System.Net.Http.HttpClient(new Handler(_=>new(System.Net.HttpStatusCode.Redirect)))){
   var answer=await WebProbe.Check(client,"https://www.youtube.com/",false,default);Check(!answer.ok&&answer.detail.Contains("перенаправление"),"redirect is inconclusive, not success");}
  using(var client=new System.Net.Http.HttpClient(new Handler(_=>new(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.StringContent("<html>blocked</html>")}))){
   var answer=await WebProbe.Check(client,"https://www.youtube.com/",false,default);Check(!answer.ok,"HTTP 200 blockpage rejected");}
  using(var client=new System.Net.Http.HttpClient(new Handler(_=>new(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.StringContent("{\"url\":\"wss://gateway.discord.gg\"}")}))){
   var answer=await WebProbe.Check(client,"https://discord.com/api/v10/gateway",true,default);Check(answer.ok,"Discord gateway validated");}
  engine=new();probe=new(No,No,Yes,Yes);
  result=await new AutoSelector(engine,probe).SelectAsync([A,B],null,true,_=>{},default);
  Check(result.Strategy==A&&engine.Starts.Count==1,"transient failure retries same strategy");
  engine=new();probe=new(No,No,Yes,Yes);
  result=await new AutoSelector(engine,probe).SelectAsync([A,B],"A",true,_=>{},default);
  Check(result.Strategy==B,"cached strategy is revalidated and replaced");
  Check(ProbeDns.Parse("{\"Status\":0,\"Answer\":[{\"type\":1,\"data\":\"1.1.1.1\"},{\"type\":1,\"data\":\"127.0.0.1\"},{\"type\":1,\"data\":\"192.168.1.1\"}]}").Length==1,"DoH rejects loopback and LAN addresses");
  Check(ProbeDns.Parse("{\"Status\":3}").Length==0,"DoH NXDOMAIN not treated as success");
  Console.WriteLine($"TOTAL {passed} passed");
  if(args.Contains("--live")) {var live=await new WebProbe().CheckAsync(default);Console.WriteLine($"LIVE YouTube={live.YouTubeDetail}; Discord={live.DiscordDetail}");}
 }
 class Handler(Func<System.Net.Http.HttpRequestMessage,System.Net.Http.HttpResponseMessage> action):System.Net.Http.HttpMessageHandler{
  protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(action(request));}
 }
 class StreamingPage:Stream {
  bool read;
  public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}
  public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){
   if(read)throw new IOException("Probe incorrectly waited for more data");read=true;var bytes=System.Text.Encoding.UTF8.GetBytes("<html>ytcfg.set({});");bytes.CopyTo(buffer);return ValueTask.FromResult(bytes.Length);
  }
  public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
 }
 class FakeEngine:IEngine {
  public bool Running{get;private set;} public List<string> Starts=new();public string? Fail;public bool Denied;
  public Task StartAsync(Strategy s,CancellationToken token){token.ThrowIfCancellationRequested();Starts.Add(s.Name);if(Denied)throw new UnauthorizedAccessException();if(s.Name==Fail)throw new IOException();if(Running)throw new Exception("parallel engines");Running=true;return Task.CompletedTask;}
  public Task StopAsync(){Running=false;return Task.CompletedTask;}
 }
 class FakeProbe(params ProbeResult[] values):IProbe {
  int n;public int BlockAfter=int.MaxValue;
  public async Task<ProbeResult> CheckAsync(CancellationToken token){token.ThrowIfCancellationRequested();if(n>=BlockAfter)await Task.Delay(Timeout.Infinite,token);if(n>=values.Length)throw new Exception("unexpected probe");return values[n++];}
 }
}
