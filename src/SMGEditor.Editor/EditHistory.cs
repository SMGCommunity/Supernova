namespace SMGEditor.Editor;

internal sealed class EditHistory
{
    private readonly List<(Action Undo, Action Redo)> _undo = [];
    private readonly List<(Action Undo, Action Redo)> _redo = [];
    private int _savedDepth;

    private const int MaxDepth = 200;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsDirty => _undo.Count != _savedDepth;

    public void Push(Action undo, Action redo)
    {
        _undo.Add((undo, redo));
        if (_undo.Count > MaxDepth)
        {
            _undo.RemoveAt(0);

            if (_savedDepth > 0)
            {
                _savedDepth--;
            }
            else
            {
                _savedDepth = -1;
            }
        }

        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        (Action Undo, Action Redo) entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        entry.Undo();
        _redo.Add(entry);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        (Action Undo, Action Redo) entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        entry.Redo();
        _undo.Add(entry);
    }

    public void MarkSaved() => _savedDepth = _undo.Count;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _savedDepth = 0;
    }
}
