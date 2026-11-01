using Godot;
using System;
using System.Collections.Generic;

namespace Game.UI;

/// <summary>
/// Draws the background styleboxes of many controls in ONE canvas item placed under them.
///
/// Why (measured in the live game, 2026-10-28): Godot batches consecutive draw commands
/// that share a texture. A Button draws [stylebox][icon][text], so every button switches
/// texture several times and the next button starts a new batch: the HUD cost ~35 draw
/// calls, the F3 panel ~100. With the backgrounds moved into one item underneath, all
/// backgrounds become one batch and the icons/glyphs of neighbouring controls batch too.
///
/// Look is kept 1:1: the original stylebox of every state is captured and drawn at the
/// control's rect; the control gets a StyleBoxEmpty with the same content margins, so
/// layout does not move. Visibility, hover/pressed/disabled state, Flat and Modulate of
/// the control and its parents are respected every frame.
///
/// Rules for callers:
/// - Only batch controls that do not overlap controls drawn earlier (rows of buttons,
///   HUD bars, fixed panels). Popups that cover the HUD must be skipped.
/// - After replacing a theme stylebox override of a batched control, call
///   <see cref="Refresh"/> so the new style is captured.
/// - Controls added later are not batched automatically (they keep drawing their own
///   background, which is correct, just not batched).
/// </summary>
public partial class StyleBoxBatcher : Control
{
	// Order must match BaseButton.DrawMode: Normal, Pressed, Hover, Disabled, HoverPressed.
	private static readonly StringName[] ButtonNames = { "normal", "pressed", "hover", "disabled", "hover_pressed" };
	private static readonly StringName[] PanelNames = { "panel" };
	// "focus" is drawn by the control itself on top of its content: left untouched.
	private static readonly StringName[] LineEditNames = { "normal", "read_only" };

	private sealed class Entry
	{
		public Control Target;
		public ulong Id;
		public StyleBox[] States;
	}

	private readonly List<Entry> _entries = new();
	private readonly Dictionary<ulong, Entry> _byId = new();
	private readonly HashSet<StyleBox> _ownEmpties = new();
	private readonly Dictionary<(StyleBox, Color), StyleBox> _tinted = new();

	/// <summary>Number of controls whose background this batcher draws.</summary>
	public int Count => _entries.Count;

	/// <summary>True if the control's background is drawn by this batcher.</summary>
	public bool Contains(Control c) => c != null && _byId.ContainsKey(c.GetInstanceId());

	/// <summary>
	/// Batch all supported controls under <paramref name="root"/> (excluding subtrees for
	/// which <paramref name="skip"/> returns true). One batcher is created per distinct
	/// relative ZIndex, so controls lifted with ZIndex keep being drawn above the rest.
	/// Batchers are added to <paramref name="host"/> (default: root) before all children.
	/// <paramref name="host"/> must not be a layout container (Box/Grid), it would give
	/// the batcher a slot.
	/// </summary>
	public static List<StyleBoxBatcher> Attach(Control root, Func<Control, bool> skip = null, Node host = null)
	{
		host ??= root;
		var byZ = new SortedDictionary<int, List<Control>>();
		Collect(root, 0, skip, byZ, includeSelf: host != root);

		var result = new List<StyleBoxBatcher>();
		foreach (var (z, controls) in byZ)
		{
			var batcher = new StyleBoxBatcher { Name = z == 0 ? "StyleBoxBatcher" : $"StyleBoxBatcher_z{z}", ZIndex = z };
			host.AddChild(batcher);
			host.MoveChild(batcher, 0);
			foreach (var c in controls)
				batcher.Add(c);
			result.Add(batcher);
		}
		return result;
	}

	private static void Collect(Control node, int z, Func<Control, bool> skip,
		SortedDictionary<int, List<Control>> byZ, bool includeSelf)
	{
		if (includeSelf)
		{
			if (node is StyleBoxBatcher || (skip != null && skip(node)))
				return;
			z = node.ZAsRelative ? z + node.ZIndex : node.ZIndex;
			if (StateNames(node) != null)
			{
				if (!byZ.TryGetValue(z, out var list))
					byZ[z] = list = new List<Control>();
				list.Add(node);
			}
		}
		foreach (Node child in node.GetChildren())
		{
			if (child is Control cc)
				Collect(cc, z, skip, byZ, includeSelf: true);
		}
	}

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		FocusMode = FocusModeEnum.None;
		SetAnchorsPreset(LayoutPreset.TopLeft);
		Position = Vector2.Zero;
		Size = Vector2.Zero;
	}

	/// <summary>Take over the background of one control. Unsupported types are ignored.</summary>
	public bool Add(Control target)
	{
		if (target == null || !GodotObject.IsInstanceValid(target))
			return false;
		ulong id = target.GetInstanceId();
		if (_byId.ContainsKey(id))
			return true;
		StringName[] names = StateNames(target);
		if (names == null)
			return false;

		var entry = new Entry { Target = target, Id = id, States = new StyleBox[names.Length] };
		Capture(entry, names);
		_entries.Add(entry);
		_byId[id] = entry;
		return true;
	}

	/// <summary>
	/// Re-capture the styles of a batched control after code replaced some of its theme
	/// stylebox overrides. States that were not replaced keep their captured style.
	/// Not batched yet → it is added.
	/// </summary>
	public void Refresh(Control target)
	{
		if (target == null || !GodotObject.IsInstanceValid(target))
			return;
		if (_byId.TryGetValue(target.GetInstanceId(), out var entry))
			Capture(entry, StateNames(target));
		else
			Add(target);
	}

	private static StringName[] StateNames(Control target) => target switch
	{
		Button => ButtonNames,
		PanelContainer or Panel => PanelNames,
		LineEdit => LineEditNames,
		_ => null,
	};

	private void Capture(Entry entry, StringName[] names)
	{
		for (int i = 0; i < names.Length; i++)
		{
			StyleBox current = entry.Target.GetThemeStylebox(names[i]);
			if (current != null && _ownEmpties.Contains(current))
				continue; // still our placeholder: keep the captured style
			entry.States[i] = current;
			StyleBox empty = EmptyLike(current);
			_ownEmpties.Add(empty);
			entry.Target.AddThemeStyleboxOverride(names[i], empty);
		}
	}

	public override void _Process(double delta)
	{
		// Button states and positions change at any time; redrawing one item per frame
		// is far cheaper than the batches it saves.
		if (IsVisibleInTree())
			QueueRedraw();
	}

	public override void _Draw()
	{
		Vector2 origin = GlobalPosition;
		Node inherited = GetParent(); // modulate of this node and above already applies to us
		Rid canvas = GetCanvasItem();

		for (int i = _entries.Count - 1; i >= 0; i--)
		{
			Control c = _entries[i].Target;
			if (!GodotObject.IsInstanceValid(c) || !c.IsInsideTree())
			{
				_byId.Remove(_entries[i].Id);
				_entries.RemoveAt(i);
			}
		}

		for (int i = 0; i < _entries.Count; i++)
		{
			Entry e = _entries[i];
			Control c = e.Target;
			if (!c.IsVisibleInTree())
				continue;

			StyleBox sb = PickState(e);
			if (sb == null)
				continue;

			Color tint = RelativeModulate(c, inherited);
			if (tint.A <= 0.001f)
				continue;

			// Batched controls are axis-aligned and unscaled relative to the host
			// (HUD, F3 panel): an offset is enough and keeps one transform for all.
			var rect = new Rect2(c.GlobalPosition - origin, c.Size);
			if (tint == Colors.White)
				sb.Draw(canvas, rect);
			else
				Tinted(sb, tint).Draw(canvas, rect); // StyleBox.Draw has no tint argument
		}
	}

	private static StyleBox PickState(Entry e)
	{
		switch (e.Target)
		{
			case Button b:
				if (b.Flat)
					return null; // a flat button draws no background by itself either
				int mode = (int)b.GetDrawMode();
				if (mode < 0 || mode >= e.States.Length)
					mode = 0;
				return e.States[mode] ?? e.States[0];
			case LineEdit le:
				return le.Editable ? e.States[0] : e.States[1];
			default:
				return e.States[0];
		}
	}

	/// <summary>
	/// Modulate the control's own background would get, without the part the batcher
	/// already inherits: SelfModulate and Modulate of the control, Modulate of its parents
	/// up to (not including) <paramref name="inherited"/>.
	/// </summary>
	private static Color RelativeModulate(Control c, Node inherited)
	{
		Color m = c.SelfModulate * c.Modulate;
		Node n = c.GetParent();
		while (n != null && n != inherited && n is CanvasItem ci)
		{
			m *= ci.Modulate;
			n = n.GetParent();
		}
		return m;
	}

	private StyleBox Tinted(StyleBox sb, Color tint)
	{
		if (_tinted.TryGetValue((sb, tint), out var copy))
			return copy;
		copy = (StyleBox)sb.Duplicate();
		switch (copy)
		{
			case StyleBoxFlat f:
				f.BgColor *= tint;
				f.BorderColor *= tint;
				f.ShadowColor *= tint;
				break;
			case StyleBoxTexture t:
				t.ModulateColor *= tint;
				break;
			case StyleBoxLine l:
				l.Color *= tint;
				break;
		}
		if (_tinted.Count > 256)
			_tinted.Clear(); // tints are a handful of fixed colors; guard against growth
		_tinted[(sb, tint)] = copy;
		return copy;
	}

	/// <summary>Empty stylebox with the same content margins, so layout stays the same.</summary>
	private static StyleBox EmptyLike(StyleBox src)
	{
		var empty = new StyleBoxEmpty();
		if (src != null)
		{
			empty.ContentMarginLeft = src.GetMargin(Side.Left);
			empty.ContentMarginTop = src.GetMargin(Side.Top);
			empty.ContentMarginRight = src.GetMargin(Side.Right);
			empty.ContentMarginBottom = src.GetMargin(Side.Bottom);
		}
		return empty;
	}
}
