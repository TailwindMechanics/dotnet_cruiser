//path: src\Controllers\BulletinController.cs

using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using System.Reactive.Linq;
using System.Reactive;
using Newtonsoft.Json;
using System.Text;
using Serilog;

using Neurocache.NodeRouter;
using Neurocache.Schema;

namespace Neurocache.Controllers
{
    [ApiController]
    public class Controller : ControllerBase
    {
        [HttpPost("kill")]
        public IActionResult Kill()
        {
            Log.Information("Killing all sessions");
            BulletinRouter.KillSubject.OnNext(Unit.Default);
            return Ok($"Killed");
        }

        [HttpPost("stop")]
        public IActionResult StopAgent([FromBody] StopSessionRequest body)
        {
            body.Deconstruct(out var sessionToken);
            Log.Information($"Stopping session: {sessionToken}");
            BulletinRouter.StopSubject.OnNext(sessionToken);
            return Ok($"Stopped session: {sessionToken}");
        }

        [HttpPost("run")]
        public async Task Run()
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var bulletin = JsonConvert.DeserializeObject<Bulletin>(body)!;

            int completedNodeCount = 0;
            var channel = Channel.CreateUnbounded<string>();

            Log.Information($"Received bulletin");
            BulletinRouter.RecordStream
                .Where(rec => rec.SessionToken == bulletin.SessionToken)
                .Subscribe(rec =>
                {
                    if (rec.IsFinal) completedNodeCount++;

                    var serialized = JsonConvert.SerializeObject(rec);
                    channel.Writer.TryWrite(serialized);

                    Log.Information($"Emitting record:{rec.Payload}, from:{rec.NodeId}, is final:{rec.IsFinal}");
                    if (completedNodeCount >= bulletin.NodeIds.Count)
                    {
                        Log.Information("All nodes completed, closing channel");
                        channel.Writer.TryComplete();
                    }
                });

            BulletinRouter.BulletinSubject.OnNext(bulletin);

            var stream = Response.Body;
            Response.StatusCode = 200;
            Response.ContentType = "application/json";
            await foreach (var rec in channel.Reader.ReadAllAsync())
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(rec));
                await stream.FlushAsync();
            }
        }
    }
}