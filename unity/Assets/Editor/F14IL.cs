using System; using System.IO; using System.Linq; using System.Reflection; using System.Reflection.Emit; using System.Text; using System.Collections.Generic; using UnityEngine;
public static class F14IL {
  static Dictionary<short, OpCode> ops;
  public static void Dump() {
    ops = new Dictionary<short, OpCode>();
    foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static)) { var o=(OpCode)f.GetValue(null); ops[o.Value]=o; }
    var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
    var sb = new StringBuilder();
    foreach (var line in File.ReadAllLines("E:/Project/NOModding/F14Bridge/il.txt")) {
      var p = line.Trim().Split('.'); if (p.Length < 2) continue;
      var t = asm.GetTypes().FirstOrDefault(x => x.FullName == p[0].Replace('+','+'));
      if (t == null) { sb.AppendLine("no type " + p[0]); continue; }
      foreach (var m in t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Where(m => m.Name == p[1])) {
        sb.AppendLine("=== " + t.FullName + "." + m.Name);
        var body = m.GetMethodBody(); if (body == null) continue;
        var il = body.GetILAsByteArray(); var mod = m.Module; int i = 0;
        while (i < il.Length) {
          int at = i; OpCode op; short v = il[i++];
          if (v == 0xFE) { v = (short)(0xFE00 | il[i++]); }
          op = ops[v]; string arg = "";
          switch (op.OperandType) {
            case OperandType.InlineNone: break;
            case OperandType.ShortInlineBrTarget: arg = "-> " + (i + 1 + (sbyte)il[i]); i += 1; break;
            case OperandType.ShortInlineI: case OperandType.ShortInlineVar: arg = il[i].ToString(); i += 1; break;
            case OperandType.InlineVar: arg = BitConverter.ToInt16(il, i).ToString(); i += 2; break;
            case OperandType.InlineBrTarget: arg = "-> " + (i + 4 + BitConverter.ToInt32(il, i)); i += 4; break;
            case OperandType.InlineI: arg = BitConverter.ToInt32(il, i).ToString(); i += 4; break;
            case OperandType.InlineI8: arg = BitConverter.ToInt64(il, i).ToString(); i += 8; break;
            case OperandType.ShortInlineR: arg = BitConverter.ToSingle(il, i).ToString("R"); i += 4; break;
            case OperandType.InlineR: arg = BitConverter.ToDouble(il, i).ToString("R"); i += 8; break;
            case OperandType.InlineSwitch: { int n = BitConverter.ToInt32(il, i); i += 4 + 4 * n; arg = "switch" + n; break; }
            default: { int tok = BitConverter.ToInt32(il, i); i += 4;
              try { if (op.OperandType == OperandType.InlineField) { var f = mod.ResolveField(tok); arg = f.DeclaringType.Name + "::" + f.Name; }
                else if (op.OperandType == OperandType.InlineMethod) { var mm = mod.ResolveMethod(tok); arg = mm.DeclaringType.Name + "::" + mm.Name; }
                else if (op.OperandType == OperandType.InlineType) { arg = mod.ResolveType(tok).Name; }
                else if (op.OperandType == OperandType.InlineString) { arg = "\"" + mod.ResolveString(tok) + "\""; }
                else { var mb = mod.ResolveMember(tok); arg = mb.DeclaringType.Name + "::" + mb.Name; } } catch { arg = "tok" + tok; } break; }
          }
          sb.AppendLine("  " + at.ToString("D4") + " " + op.Name + " " + arg);
        }
      }
    }
    File.WriteAllText("E:/Project/NOModding/F14Bridge/il_out.txt", sb.ToString());
    Debug.Log("[F14] IL dumped");
  }
}
