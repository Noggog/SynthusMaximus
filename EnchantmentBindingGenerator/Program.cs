﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using SynthusMaximus.Data.DTOs.Enchantment;
using Wabbajack.Common;

namespace EnchantmentBindingGenerator
{
    class Program
    {
        static Lazy<Settings> _LazySettings = null!;
        static Settings Settings => _LazySettings.Value;
        
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings(nickname: "Settings",
                    path: "settings.json",
                    out _LazySettings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "dummy.esp")
                .Run(args);
        }
        
        public static async Task RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var mod = state.LoadOrder.PriorityOrder
                .FirstOrDefault(m => m.ModKey.FileName == Settings.ExportMod);

            if (mod == null)
            {
                Console.WriteLine($"Could not locate {Settings.ExportMod} in the load order");
            }
            
            
            Console.WriteLine("Finding armors");
            var query = from armor in mod!.Mod!.Armors.AsParallel()
                where !armor.ObjectEffect.IsNull
                from list in mod!.Mod.LeveledItems
                from thisEntry in list.Entries.EmptyIfNull()
                where thisEntry.Data!.Reference.FormKey == armor.FormKey
                from otherEntry in list.Entries.EmptyIfNull()
                where otherEntry.Data!.Count == thisEntry.Data!.Count
                where otherEntry.Data!.Level == thisEntry.Data!.Level
                let resolvedOther = otherEntry.Data!.Reference.TryResolve<IArmorGetter>(state.LinkCache)
                where resolvedOther != null
                where !resolvedOther.ObjectEffect.IsNull
                let resolvedOtherEnchantment = resolvedOther.ObjectEffect.TryResolve<IObjectEffectGetter>(state.LinkCache)
                where resolvedOtherEnchantment != null
                let resolvedThisEnchantment = armor.ObjectEffect.TryResolve<IObjectEffectGetter>(state.LinkCache)
                where resolvedThisEnchantment != null
                where resolvedOtherEnchantment.FormKey.ModKey != resolvedThisEnchantment.FormKey.ModKey
                let binding = new EnchantmentReplacer
                {
                    EdidBase = resolvedOtherEnchantment.EditorID,
                    BaseModKey = resolvedOtherEnchantment.FormKey.ModKey,
                    EdidNew = resolvedThisEnchantment.EditorID
                }
                where binding.EdidBase != binding.EdidNew
                group (list.EditorID, binding) by list.EditorID
                into grouped
                select new ListEnchantmentBinding
                {
                    EdidList = grouped.Key,
                    FillListWithSimilars = true,
                    Replacers = (from entry in grouped
                        group (entry) by entry.binding.EdidNew into subGroup
                        select subGroup.First().binding).ToList()
                };
            var data = query.ToArray();

            var filename = AbsolutePath.EntryPoint.Combine("config", Settings.ExportMod, "enchanting",
                "listEnchantmentBindings.json");
            filename.Parent.CreateDirectory();
            Console.WriteLine($"Writing data to {filename}");
            await data.ToJsonAsync(filename, useGenericSettings: true, prettyPrint: true);
        }
    }
}