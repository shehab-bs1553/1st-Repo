#pragma warning disable IDE0005 // Using directive is unnecessary.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#pragma warning restore IDE0005 // Using directive is unnecessary.

namespace SkSemanticSearch
{
    public class ChatHistory
    {
        private readonly List<string> _history = new List<string>();

        public void AddUserMessage(string message)
        {
            _history.Add($"User: {message}");
        }

        public void AddBotMessage(string message)
        {
            _history.Add($"Bot: {message}");
        }

        public string GetHistory()
        {
            return string.Join("\n", _history);
        }
    }
}
