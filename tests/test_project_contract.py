import json
import pathlib
import re
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


class DPSMeterProjectContract(unittest.TestCase):
    def read_json(self, relative_path):
        with (ROOT / relative_path).open(encoding="utf-8") as handle:
            return json.load(handle)

    def read_text(self, relative_path):
        return (ROOT / relative_path).read_text(encoding="utf-8")

    def test_local_mod_descriptor_is_display_only(self):
        descriptor = self.read_json("DPSMeter.json")
        self.assertEqual(descriptor["id"], "DPSMeter")
        self.assertEqual(descriptor["name"], "DPS Meter")
        self.assertIs(descriptor["has_dll"], True)
        self.assertIs(descriptor["has_pck"], False)
        self.assertIs(descriptor["affects_gameplay"], False)
        self.assertEqual(descriptor["dependencies"], [])

    def test_pack_manifest_matches_project_identity(self):
        manifest = self.read_json("mod_manifest.json")
        self.assertEqual(manifest["pck_name"], "DPSMeter")
        self.assertEqual(manifest["name"], "DPS Meter")
        self.assertIn("damage", manifest["description"].lower())

    def test_english_only_localization_is_packaged(self):
        loc = self.read_json("assets/localization/eng/dps_meter.json")
        self.assertEqual(loc["TITLE"], "DPS Meter")
        self.assertEqual(loc["COMBAT"], "Combat")
        self.assertEqual(loc["TOTAL"], "Total")
        self.assertFalse((ROOT / "assets/localization/zhs").exists())

    def test_readme_documents_manual_local_install_not_workshop_only(self):
        readme = self.read_text("README.md")
        self.assertIn("Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/DPSMeter/", readme)
        self.assertIn('"affects_gameplay": false', readme)
        self.assertIn("manual", readme.lower())
        self.assertIn("Steam Workshop", readme)

    def test_license_and_notice_preserve_upstream_attribution(self):
        license_text = self.read_text("LICENSE")
        notice = self.read_text("NOTICE")
        self.assertIn("MIT License", license_text)
        self.assertIn("BAIGUANGMEI", license_text)
        self.assertIn("BAIGUANGMEI/STS2-DamageTracker", notice)
        self.assertIn("MIT", notice)

    def test_source_uses_dps_meter_identity_and_display_only_storage(self):
        mod_entry = self.read_text("src/ModEntry.cs")
        service = self.read_text("src/RunDPSMeterService.cs")
        overlay = self.read_text("src/DPSMeterOverlay.cs")
        csproj = self.read_text("DPSMeter.csproj")
        self.assertIn('new Harmony("local.sts2.dps_meter")', mod_entry)
        self.assertNotIn('"DPSMeter.pck"', mod_entry)
        self.assertIn('DefaultLocStrings', overlay)
        self.assertIn('"user://dps_meter_state.json"', service)
        self.assertIn('res://assets/localization/eng/dps_meter.json', overlay)
        self.assertIn("<Project Sdk=\"Godot.NET.Sdk/4.5.1\">", csproj)



    def test_csproj_defaults_support_macos_steam_install(self):
        csproj = self.read_text("DPSMeter.csproj")
        self.assertIn("SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64", csproj)
        self.assertIn("SlayTheSpire2.app/Contents/Resources/data_sts2_macos_x86_64", csproj)
        self.assertIn("$(HOME)/Library/Application Support/Steam/steamapps/common/Slay the Spire 2", csproj)
        self.assertIn("<Sts2ModsDir", csproj)
        self.assertIn("$(Sts2Dir)/SlayTheSpire2.app/Contents/MacOS/mods/$(MSBuildProjectName)/", csproj)

    def test_csproj_has_clear_sts2_reference_preflight(self):
        csproj = self.read_text("DPSMeter.csproj")
        self.assertIn('Name="ValidateSts2References"', csproj)
        self.assertIn('BeforeTargets="ResolveReferences"', csproj)
        self.assertIn('Missing Slay the Spire 2 assemblies', csproj)

    def test_player_display_name_resolution_prefers_network_names_over_character_labels(self):
        helpers = self.read_text("src/ReflectionHelpers.cs")
        self.assertIn("TryGetPlatformDisplayName", helpers)
        self.assertNotIn("typedPlayer.Creature.Name", helpers)
        self.assertNotIn("typedPlayer.Character.Title", helpers)

        match = re.search(r"PlayerNameMembers\s*=\s*\{(?P<body>.*?)\};", helpers, re.S)
        self.assertIsNotNone(match, "Player display-name member list should be explicit")
        body = match.group("body")
        self.assertIn('"DisplayName"', body)
        self.assertIn('"PlayerName"', body)
        self.assertIn('"PersonaName"', body)
        self.assertNotIn('"CharacterName"', body)
        self.assertNotIn('"LocalizedName"', body)


    def test_v02_hooks_collect_card_usage_and_received_damage(self):
        mod_entry = self.read_text("src/ModEntry.cs")
        service = self.read_text("src/RunDPSMeterService.cs")
        overlay = self.read_text("src/DPSMeterOverlay.cs")
        loc = self.read_json("assets/localization/eng/dps_meter.json")

        for hook_name in ["AfterCardPlayed", "AfterDamageReceived", "AfterBlockGained"]:
            self.assertIn(f"nameof(Hook.{hook_name})", mod_entry)

        self.assertIn("RecordCardPlayed", service)
        self.assertIn("RecordDamageReceived", service)
        self.assertIn("RecordBlockGained", service)
        for field in ["CardsPlayed", "AttackCardsPlayed", "SkillCardsPlayed", "PowerCardsPlayed", "AutoCardsPlayed"]:
            self.assertIn(field, service)
        for field in ["IncomingDamage", "BlockedDamage", "HpLostDamage", "BlockGained"]:
            self.assertIn(field, service)

        self.assertEqual(loc["TAB_METER"], "Meter")
        self.assertEqual(loc["TAB_CARDS"], "Card Usage")
        self.assertEqual(loc["TAB_RECEIVED"], "Received Damage")
        for label in ["CARD_USAGE", "RECEIVED_DAMAGE", "HP_LOST", "BLOCKED", "INCOMING", "AUTO", "BLOCK_GAINED"]:
            self.assertIn(label, loc)
            self.assertIn(label, overlay)

    def test_v02_version_is_declared(self):
        descriptor = self.read_json("DPSMeter.json")
        manifest = self.read_json("mod_manifest.json")
        self.assertEqual(descriptor["version"], "0.2.3")
        self.assertEqual(manifest["version"], "0.2.3")


    def test_max_damage_uses_card_play_aggregation_for_multi_hit_and_aoe(self):
        mod_entry = self.read_text("src/ModEntry.cs")
        service = self.read_text("src/RunDPSMeterService.cs")

        self.assertIn("nameof(Hook.BeforeCardPlayed)", mod_entry)
        self.assertIn("BeginCardDamageAggregation", service)
        self.assertIn("CompleteCardDamageAggregation", service)
        self.assertIn("CardDamageAggregationContext", service)
        self.assertIn("TryAddToActiveCardDamageAggregation", service)
        self.assertRegex(service, r"CompleteCardDamageAggregation[\s\S]*?MaxHitDamage")


    def test_power_and_pet_damage_are_attributed_to_the_owning_player(self):
        mod_entry = self.read_text("src/ModEntry.cs")
        service = self.read_text("src/RunDPSMeterService.cs")
        helpers = self.read_text("src/ReflectionHelpers.cs")

        self.assertIn("PatchPowerDamageMethod", mod_entry)
        for power_type in [
            "HauntPower",
            "StranglePower",
            "DemisePower",
            "FlameBarrierPower",
            "ConstrictPower",
            "DisintegrationPower",
            "ThornsPower",
            "ReflectPower",
        ]:
            self.assertIn(f"typeof({power_type})", mod_entry)

        self.assertIn("SourceDamageTrackingContext", service)
        self.assertIn("CurrentSourceDamageContext", service)
        self.assertIn("HasActivePlayerDamageSource", service)
        self.assertRegex(service, r"RecordDamage[\s\S]*?TryResolveCurrentDamageSourceHandle")
        self.assertRegex(mod_entry, r"AfterDamageGivenPostfix[\s\S]*?HasActivePlayerDamageSource")

        self.assertIn("creature.PetOwner", helpers)


    def test_damage_dealt_excludes_player_targets_but_received_tracks_lethal_hits(self):
        mod_entry = self.read_text("src/ModEntry.cs")
        service = self.read_text("src/RunDPSMeterService.cs")

        self.assertRegex(service, r"RecordDamage[\s\S]*?IsPlayerDamageTarget")
        self.assertIn("RecordDamageReceivedFromDamageGiven", service)
        self.assertRegex(service, r"RecordDamageReceivedFromDamageGiven[\s\S]*?WasTargetKilled")
        self.assertRegex(mod_entry, r"AfterDamageGivenPostfix[\s\S]*?RecordDamageReceivedFromDamageGiven")



    def test_overlay_creation_is_not_limited_to_new_combat_start(self):
        mod_entry = self.read_text("src/ModEntry.cs")

        self.assertRegex(mod_entry, r"Initialize[\s\S]*?DPSMeterOverlay\.EnsureCreated\(\)")
        self.assertIn("EnsureOverlayCreated", mod_entry)
        self.assertRegex(mod_entry, r"AfterPlayerTurnStartPostfix[\s\S]*?EnsureOverlayCreated\(\)")
        self.assertRegex(mod_entry, r"AfterDamageGivenPostfix[\s\S]*?EnsureOverlayCreated\(\)")

    def test_run_end_resets_meter_and_persisted_state(self):
        mod_entry = self.read_text("src/ModEntry.cs")
        service = self.read_text("src/RunDPSMeterService.cs")

        self.assertIn("typeof(RunManager)", mod_entry)
        self.assertIn("nameof(RunManager.OnEnded)", mod_entry)
        self.assertIn("RunEndedPostfix", mod_entry)
        self.assertIn("RunDPSMeterService.EndRun", mod_entry)
        self.assertIn("public static void EndRun", service)
        reset = self.read_text("src/KillTrackingService.cs")
        self.assertRegex(service, r"EndRun[\s\S]*?ResetTrackingMemory")
        self.assertIn("Totals.Clear()", reset)
        self.assertIn("_currentRunToken = null", reset)
        self.assertIn("_stableRunId = null", reset)
        self.assertRegex(service, r"EndRun[\s\S]*?DeletePersistedState")
        self.assertRegex(service, r"DeletePersistedState[\s\S]*?File\.Delete")

    def test_source_comments_are_english_only(self):
        for relative_path in ["src/ModEntry.cs", "src/ReflectionHelpers.cs", "src/RunDPSMeterService.cs", "src/DPSMeterOverlay.cs"]:
            self.assertIsNone(re.search(r"[\u4e00-\u9fff]", self.read_text(relative_path)), relative_path)

    def test_no_obvious_gameplay_mutation_terms_in_mod_entry(self):
        mod_entry = self.read_text("src/ModEntry.cs")
        forbidden_patterns = [
            r"\.CurrentHp\s*=",
            r"\.Gold\s*=",
            r"\.Block\s*=",
            r"Random\(",
        ]
        for pattern in forbidden_patterns:
            self.assertIsNone(re.search(pattern, mod_entry), pattern)


if __name__ == "__main__":
    unittest.main()
