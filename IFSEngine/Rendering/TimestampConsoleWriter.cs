#nullable enable
using System;
using System.IO;
using System.Text;

public class TimestampConsoleWriter : TextWriter
{
    private readonly TextWriter _originalOut;
    private bool _isAtStartOfLine = true;

    public TimestampConsoleWriter(TextWriter originalOut)
    {
        _originalOut = originalOut;
    }

    public override Encoding Encoding => _originalOut.Encoding;

    public override void Write(char value)
    {
        if (_isAtStartOfLine)
        {
            _originalOut.Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ");
            _isAtStartOfLine = false;
        }

        _originalOut.Write(value);

        if (value == '\n')
        {
            _isAtStartOfLine = true;
        }
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;

        foreach (char c in value)
        {
            Write(c);
        }
    }
}
#nullable disable