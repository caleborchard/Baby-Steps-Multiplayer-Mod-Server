using System;
using System.IO;
using System.Text;

namespace BabyStepsMultiplayerServer
{
    public class TimestampedTextWriter : TextWriter
    {
        private readonly TextWriter _originalOut;

        public TimestampedTextWriter(TextWriter originalOut)
        {
            _originalOut = originalOut;
        }
        public override Encoding Encoding => _originalOut.Encoding;
        public override void Write(string? value)
        {
            if (value != null)
                _originalOut.Write(AddTimestamp(value));
            else
                _originalOut.Write(value);
        }
        public override void WriteLine(string? value)
        {
            if (value != null)
                _originalOut.WriteLine(AddTimestamp(value));
            else
                _originalOut.WriteLine(value);
        }
        private string AddTimestamp(string text)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            return $"[{timestamp}] {text}";
        }
    }
}
