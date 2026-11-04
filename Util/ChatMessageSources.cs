using System;
using CP_SDK.Chat.Interfaces;

namespace Shaffuru.Util {
	public interface IChatMessageSource : IDisposable {
		delegate void IncomingChatMessage(string name, string message, object channel = null);

		public event IncomingChatMessage OnTextMessageReceived;

		public void SendChatMessage(string message, object channel = null);
	}

	class ChatPlexSource : IChatMessageSource {
		public event IChatMessageSource.IncomingChatMessage OnTextMessageReceived;

		bool disposed;

		ChatPlexSource() {
			CP_SDK.Chat.Service.Acquire();
			CP_SDK.Chat.Service.Multiplexer.OnTextMessageReceived += Multiplexer_OnTextMessageReceived;
		}

		void Multiplexer_OnTextMessageReceived(IChatService _, IChatMessage msg) {
			OnTextMessageReceived?.Invoke(msg.Sender.UserName, msg.Message, msg.Channel);
		}

		public void SendChatMessage(string message, object channel) {
			if(channel is IChatChannel chatChannel)
				CP_SDK.Chat.Service.Multiplexer?.SendTextMessage(chatChannel, message);
		}

		public void Dispose() {
			if(disposed)
				return;
			disposed = true;
			var multiplexer = CP_SDK.Chat.Service.Multiplexer;
			if(multiplexer != null)
				multiplexer.OnTextMessageReceived -= Multiplexer_OnTextMessageReceived;
			CP_SDK.Chat.Service.Release();
		}
	}
}
