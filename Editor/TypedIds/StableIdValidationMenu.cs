using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Foundation.TypedIds.Editor
{
    public static class StableIdValidationMenu
    {
        [MenuItem("Foundation/Typed IDs/Validate All IDs")]
        public static void ValidateAll()
        {
            List<StableIdIssue> issues;
            try
            {
                issues = Validate(includeReferences: true, showProgress: true);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[TypedIds] Validation cancelled; results are incomplete.");
                return;
            }

            foreach (var issue in issues)
                Debug.LogError("[TypedIds] " + issue, issue.Context);

            Debug.Log(issues.Count == 0
                ? "[TypedIds] All typed ids are valid."
                : $"[TypedIds] {issues.Count} typed id problem(s) found.");
        }

        public static List<StableIdIssue> Validate(bool includeReferences, bool showProgress)
        {
            var all = new List<StableIdIssue>();
            try
            {
                StableIdCatalog.Invalidate();
                foreach (var idType in StableIdCatalog.GetIdTypes())
                {
                    var entries = StableIdCatalog.GetEntries(idType);
                    all.AddRange(StableIdValidator.ValidateDefinitions(entries));
                    if (!includeReferences)
                        continue;

                    var references = StableIdReferenceScanner.Scan(idType, !showProgress ? null : (path, progress) =>
                        EditorUtility.DisplayCancelableProgressBar("Scanning " + idType.Name, path, progress));
                    all.AddRange(StableIdValidator.ValidateReferences(entries, references));
                }
            }
            finally
            {
                if (showProgress)
                    EditorUtility.ClearProgressBar();
            }

            return all;
        }
    }
}
