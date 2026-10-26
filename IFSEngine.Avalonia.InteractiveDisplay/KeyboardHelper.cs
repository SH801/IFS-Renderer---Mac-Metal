using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace IFSEngine.Avalonia.InteractiveDisplay;

//KeyboardDelay workaround
//https://codereview.stackexchange.com/questions/44404/preventing-keydown-delay
public class KeyboardHelper
{
    private readonly HashSet<Key> _pressedKeys;
    private readonly Lock _pressedKeysLock = new();
    
    public KeyboardHelper(TopLevel c)
    {
        c.LostFocus += LostFocus;
        c.KeyDown += AvaloniaKeyDown;
        c.KeyUp += AvaloniaKeyUp;
        _pressedKeys = [];
    }

    private void LostFocus(object? sender, RoutedEventArgs e)
    {
        lock (_pressedKeysLock)
        {
            _pressedKeys.Clear();
        }
    }

    public bool IsKeyDown(Key key)
    {
        lock (_pressedKeysLock)
        {
            return _pressedKeys.Contains(key);
        }
    }

    private void AvaloniaKeyDown(object? sender, KeyEventArgs e)
    {
        lock (_pressedKeysLock)
        {
            _pressedKeys.Add(e.Key);
        }
    }

    private void AvaloniaKeyUp(object? sender, KeyEventArgs e)
    {
        lock (_pressedKeysLock)
        {
            _pressedKeys.Remove(e.Key);
        }
    }
}