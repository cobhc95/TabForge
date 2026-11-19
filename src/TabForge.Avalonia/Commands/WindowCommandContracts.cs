using System;
using TabForge.Avalonia.Views;

namespace TabForge.Avalonia;

public interface IWindowCommandRegistration
{
    void Add(string commandId, Action handler);
    void AddWhen(string commandId, Action handler, Func<bool> applies);
    void AddWithArgument(string commandId, Action<string> handler);
    void AddCheckState(string commandId, Func<bool> isOn);
}

public interface IWindowCommandState
{
    bool Applies(string commandId);
    bool? CheckStateOf(string commandId);
}

public interface IWindowCommandDispatch : IMenuCommandRunner, IWindowCommandState
{
}
