namespace CanonScanStudio.Services;

public interface IUndoService
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    void Execute(IEditCommand command);
    void Undo();
    void Redo();
    void Clear();
}

public interface IEditCommand
{
    string Name { get; }
    void Execute();
    void Undo();
}

public sealed class UndoService : IUndoService
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Execute(IEditCommand command)
    {
        command.Execute();
        _undo.Push(command);
        _redo.Clear();
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        var command = _undo.Pop();
        command.Undo();
        _redo.Push(command);
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        var command = _redo.Pop();
        command.Execute();
        _undo.Push(command);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

public sealed class DelegateCommand : IEditCommand
{
    private readonly Action _execute;
    private readonly Action _undo;

    public DelegateCommand(string name, Action execute, Action undo)
    {
        Name = name;
        _execute = execute;
        _undo = undo;
    }

    public string Name { get; }
    public void Execute() => _execute();
    public void Undo() => _undo();
}
