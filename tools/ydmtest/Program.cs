using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;
using YuudongDeepMining;

public static class Program
{
    static readonly Assembly Game = typeof(Thing).Assembly;
    static readonly Assembly Mod = typeof(DeepMiningMod).Assembly;
    static readonly string[] Namespaces = { "", "Verse.", "RimWorld.", "Verse.AI.", "Verse.AI.Group.", "RimWorld.Planet.", "RimWorld.QuestGen." };
    static int failed;

    static Type Resolve(string name)
    {
        foreach (Assembly a in new[] { Mod, Game })
        {
            foreach (string ns in Namespaces)
            {
                Type t = a.GetType(ns + name);
                if (t != null) return t;
            }
        }
        return null;
    }

    static void Fail(string msg) { failed++; Console.WriteLine("FAIL " + msg); }

    // 정의 XML의 필드 이름이 실제 클래스에 있는지 (한두 단계까지).
    static readonly Dictionary<string, Type> Nested = new Dictionary<string, Type>
    {
        { "building", typeof(BuildingProperties) }, { "portal", typeof(MapPortalProperties) }, { "pocketMapProperties", typeof(PocketMapProperties) },
        { "graphicData", typeof(GraphicData) },
    };

    static void CheckFields(XmlElement el, Type type, string path)
    {
        foreach (XmlNode n in el.ChildNodes)
        {
            if (!(n is XmlElement child) || child.Name == "li") continue;
            FieldInfo f = AccessTools.Field(type, child.Name);
            if (f == null) { Fail("unknown field " + path + "." + child.Name + " on " + type.Name); continue; }
            Type sub = child.GetAttribute("Class") is string cls && cls.Length > 0 ? Resolve(cls) : null;
            if (sub == null && Nested.TryGetValue(child.Name, out Type nt)) sub = nt;
            if (sub != null && child.HasChildNodes && child.FirstChild is XmlElement) CheckFields(child, sub, path + "." + child.Name);
        }
    }

    public static int Main()
    {
        // 1) Harmony 패치가 1.7 원본에 걸리는지
        var harmony = new Harmony("ydm.test");
        var patchTypes = Mod.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Any()).ToList();
        foreach (Type t in patchTypes)
        {
            var done = new PatchClassProcessor(harmony, t).Patch();
            if (done == null || done.Count == 0) Fail("patch class applied nothing: " + t.Name);
        }
        var patched = harmony.GetPatchedMethods().ToList();
        foreach (var m in patched) Console.WriteLine("patched: " + m.DeclaringType.Name + "." + m.Name);
        Console.WriteLine("patch classes: " + patchTypes.Count + ", patched methods: " + patched.Count);

        // principle checks: 원본 실행을 막는 Prefix(bool 반환)는 허용 목록만
        var allowedSkipPrefix = new HashSet<string> { "Projectile_CanHit_Patch" };
        foreach (Type t in patchTypes)
        {
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name == "Prefix" && m.ReturnType == typeof(bool) && !allowedSkipPrefix.Contains(t.Name)) Fail("skipping prefix not allowed: " + t.Name);
            }
        }
        // principle checks: 지도·위치 필드 바꿔치기는 GhostUtility.cs 안에서만
        string src = @"D:\Study\rimworld\YuudongDeepMining\YuudongDeepMining\Source\YuudongDeepMining";
        foreach (string file in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(@"\obj\") || Path.GetFileName(file) == "GhostUtility.cs") continue;
            string text = File.ReadAllText(file);
            if (text.Contains("mapIndexOrState") || text.Contains("positionInt") || text.Contains("currentMapIndex")) Fail("map/position swap outside GhostUtility: " + Path.GetFileName(file));
        }
        Console.WriteLine("principle checks done");

        // 2) override 안 자기 재귀 (디컴파일 base 호출 실수)
        foreach (Type t in Mod.GetTypes())
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!m.IsVirtual || m.GetBaseDefinition() == m || m.GetMethodBody() == null) continue;
                List<CodeInstruction> il;
                try { il = PatchProcessor.GetOriginalInstructions(m); } catch { continue; }
                for (int i = 1; i < il.Count; i++)
                    if (il[i].opcode == OpCodes.Callvirt && il[i].operand is MethodInfo target && target.GetBaseDefinition() == m.GetBaseDefinition() && il[i - 1].opcode == OpCodes.Ldarg_0)
                        Fail("self-recursive override " + t.Name + "." + m.Name);
            }

        // 3) Defs XML: 클래스 이름과 필드 이름
        string defs = @"D:\Study\rimworld\YuudongDeepMining\YuudongDeepMining\Defs";
        int classes = 0, defsCount = 0;
        foreach (string file in Directory.GetFiles(defs, "*.xml", SearchOption.AllDirectories))
        {
            var doc = new XmlDocument();
            doc.Load(file);
            foreach (XmlElement def in doc.DocumentElement.ChildNodes.OfType<XmlElement>())
            {
                defsCount++;
                Type defType = Resolve(def.Name);
                if (defType == null) { Fail("def type " + def.Name); continue; }
                CheckFields(def, defType, def.Name + "(" + (def["defName"]?.InnerText ?? def.GetAttribute("Name")) + ")");
            }
            foreach (XmlNode n in doc.SelectNodes("//*[@Class]"))
            {
                classes++;
                if (Resolve(((XmlElement)n).GetAttribute("Class")) == null) Fail("class " + ((XmlElement)n).GetAttribute("Class"));
            }
            foreach (string tag in new[] { "thingClass", "giverClass", "driverClass", "workerClass" })
                foreach (XmlNode n in doc.GetElementsByTagName(tag))
                {
                    classes++;
                    if (Resolve(n.InnerText) == null) Fail(tag + " " + n.InnerText);
                }
            foreach (string list in new[] { "placeWorkers", "customMapComponents", "inspectorTabs" })
                foreach (XmlNode n in doc.GetElementsByTagName(list))
                    foreach (XmlNode li in n.ChildNodes.OfType<XmlElement>())
                    {
                        classes++;
                        if (Resolve(li.InnerText) == null) Fail(list + " " + li.InnerText);
                    }
        }
        Console.WriteLine("defs: " + defsCount + ", class refs: " + classes);
        // KeyCode check: 단축키 이름이 실제 KeyCode 값인지
        Type keyCode = typeof(KeyBindingDef).GetField("defaultKeyCodeA").FieldType;
        foreach (string file in Directory.GetFiles(defs, "*.xml", SearchOption.AllDirectories))
        {
            var doc = new XmlDocument(); doc.Load(file);
            foreach (XmlNode n in doc.GetElementsByTagName("defaultKeyCodeA"))
                if (!Enum.GetNames(keyCode).Contains(n.InnerText)) Fail("keycode " + n.InnerText);
                else Console.WriteLine("keycode ok " + n.InnerText);
        }

        // 4) 설정 기본값으로 단단함·곤충군락 계산
        DeepMiningMod.Settings = new DeepMiningSettings();
        for (int d = 0; d <= 6; d++)
            Console.WriteLine("depth " + d + ": hardness x" + LayerUtility.HardnessAt(d) + ", infestation mtb " + (d > 0 ? LayerUtility.InfestationMtbDays(d).ToString("0.##") + " days" : "-"));

        Console.WriteLine("failed=" + failed);
        return failed;
    }
}
