using Dalamud.Game.Chat;
using Dalamud.Game.Chat.SeStringHandling;
using Dalamud.Game.Chat.SeStringHandling.Payloads;
using Dalamud.Plugin;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using WebSocketSharp;

namespace Syrcus.Services {
  public class ChatService: Service {
    public static string Endpoint => "/chat";
    private static Queue<string> queue = new Queue<string>();

    public override void Enable () {
      Plugin.pi.Framework.Gui.Chat.OnChatMessage += OnXivMessage;
      Plugin.server.AddWebSocketService<ChatService>(Endpoint);
    }

    public override void Disable () {
      Plugin.pi.Framework.Gui.Chat.OnChatMessage -= OnXivMessage;
    }

    protected override void OnMessage (MessageEventArgs e) {
      queue.Enqueue(e.Data);
      PluginLog.Information("Enqueue: {0}", e.Data);
    }

    public override void Update () {
      if (!queue.Any()) return;
      var item = queue.Dequeue();
      PluginLog.Information("Dequeue: {0}", item);

      var entry = parseChat(item);
      if (entry != null) Plugin.pi.Framework.Gui.Chat.PrintChat(entry);
    }

    private void OnXivMessage (XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled) {
      var obj = new JObject();
      obj["type"] = (ushort) type;
      obj["senderId"] = senderId;
      obj["sender"] = sender?.TextValue;
      obj["message"] = message?.TextValue;
      obj["isHandled"] = isHandled;

      Plugin.server.WebSocketServices[Endpoint].Sessions.Broadcast(obj.ToString());
    }

    public static XivChatEntry parseChat (string json) {
      JToken data = JToken.Parse(json);

      if (data.Type == JTokenType.String) {
        return new XivChatEntry {
          MessageBytes = new SeString(new List<Payload>() {
            new TextPayload(data.ToString())
          }).Encode()
        };
      }

      if (data.Type != JTokenType.Object) {
        return null;
      }

      JObject o = data as JObject;
      XivChatEntry entry = new XivChatEntry();
      List<Payload> payloads = new List<Payload>();

      if (o.ContainsKey("type") && o["type"].Type == JTokenType.Integer) {
        entry.Type = (XivChatType) (ushort) o["type"];
      }

      if (o.ContainsKey("senderId") && o["senderId"].Type == JTokenType.Integer) {
        entry.SenderId = (uint) o["senderId"];
      }

      if (o.ContainsKey("name") && o["name"].Type == JTokenType.String) {
        entry.Name = (string) o["name"];
      }

      if (o.ContainsKey("text") && o["text"].Type == JTokenType.String) {
        payloads.Add(new TextPayload((string) o["text"]));
      }
        
      if (o.ContainsKey("payloads") && o["payloads"].Type == JTokenType.Array) {
        foreach (var p in o["payloads"] as JArray) {
          if (p.Type != JTokenType.Array) continue;
          var payload = p as JArray;

          var type = payload[0];
          if (type.Type != JTokenType.String) continue;

          if (payload.Count == 2) {
            if (type.ToString() == "Text" && payload[1].Type == JTokenType.String) {
              payloads.Add(new TextPayload((string) payload[1]));
            }

            if (type.ToString() == "UIForeground" && payload[1].Type == JTokenType.Integer) {
              payloads.Add(new UIForegroundPayload(Plugin.pi.Data, (ushort) payload[1]));
            }

            if (type.ToString() == "Icon" && payload[1].Type == JTokenType.Integer) {
              payloads.Add(new IconPayload((BitmapFontIcon) (uint) payload[1]));
            }
          }
        }
      }

      entry.MessageBytes = new SeString(payloads).Encode();
      return entry;
    }
  }
}
