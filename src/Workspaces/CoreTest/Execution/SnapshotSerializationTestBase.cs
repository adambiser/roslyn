﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Execution;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Serialization;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.UnitTests.Execution;
using Xunit;

namespace Microsoft.CodeAnalysis.UnitTests
{
    public class SnapshotSerializationTestBase
    {
        internal static Solution CreateFullSolution(HostServices hostServices = null)
        {
            var solution = new AdhocWorkspace(hostServices ?? Host.Mef.MefHostServices.DefaultHost).CurrentSolution;
            var csCode = "class A { }";
            var project1 = solution.AddProject("Project", "Project.dll", LanguageNames.CSharp);
            var document1 = project1.AddDocument("Document1", SourceText.From(csCode));

            var vbCode = "Class B\r\nEnd Class";
            var project2 = document1.Project.Solution.AddProject("Project2", "Project2.dll", LanguageNames.VisualBasic);
            var document2 = project2.AddDocument("Document2", SourceText.From(vbCode));

            project1 = document2.Project.Solution.GetProject(project1.Id).AddProjectReference(new ProjectReference(project2.Id, ImmutableArray.Create("test")));
            project1 = project1.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            project1 = project1.AddAnalyzerReference(new AnalyzerFileReference(typeof(object).Assembly.Location, new TestAnalyzerAssemblyLoader()));

            var textDocument1 = project1.AddAdditionalDocument("Additional", SourceText.From("hello"), ImmutableArray.Create("test"), @".\Add");
            return textDocument1.Project.Solution;
        }

        internal static async Task VerifyAssetAsync(ISolutionSynchronizationService service, SolutionStateChecksums solutionObject)
        {
            await VerifyAssetSerializationAsync<SerializedSolutionInfo>(
                service, solutionObject.Info, WellKnownSynchronizationKinds.SolutionInfo,
                (v, k, s) => SolutionAsset.Create(s.CreateChecksum(v, CancellationToken.None), v, s)).ConfigureAwait(false);

            foreach (var projectChecksum in solutionObject.Projects)
            {
                var projectObject = await service.GetValueAsync<ProjectStateChecksums>(projectChecksum).ConfigureAwait(false);
                await VerifyAssetAsync(service, projectObject).ConfigureAwait(false);
            }
        }

        internal static async Task VerifyAssetAsync(ISolutionSynchronizationService service, ProjectStateChecksums projectObject)
        {
            var info = await VerifyAssetSerializationAsync<SerializedProjectInfo>(
                service, projectObject.Info, WellKnownSynchronizationKinds.ProjectInfo,
                (v, k, s) => SolutionAsset.Create(s.CreateChecksum(v, CancellationToken.None), v, s)).ConfigureAwait(false);

            await VerifyAssetSerializationAsync<CompilationOptions>(
                service, projectObject.CompilationOptions, WellKnownSynchronizationKinds.CompilationOptions,
                (v, k, s) => SolutionAsset.Create(s.CreateChecksum(v, CancellationToken.None), v, s));

            await VerifyAssetSerializationAsync<ParseOptions>(
                service, projectObject.ParseOptions, WellKnownSynchronizationKinds.ParseOptions,
                (v, k, s) => SolutionAsset.Create(s.CreateChecksum(v, CancellationToken.None), v, s));

            foreach (var checksum in projectObject.Documents)
            {
                var documentObject = await service.GetValueAsync<DocumentStateChecksums>(checksum).ConfigureAwait(false);
                await VerifyAssetAsync(service, documentObject).ConfigureAwait(false);
            }

            foreach (var checksum in projectObject.ProjectReferences)
            {
                await VerifyAssetSerializationAsync<ProjectReference>(
                    service, checksum, WellKnownSynchronizationKinds.ProjectReference,
                    (v, k, s) => SolutionAsset.Create(s.CreateChecksum(v, CancellationToken.None), v, s));
            }

            foreach (var checksum in projectObject.MetadataReferences)
            {
                await VerifyAssetSerializationAsync<MetadataReference>(
                    service, checksum, WellKnownSynchronizationKinds.MetadataReference,
                    (v, k, s) => SolutionAsset.Create(s.CreateChecksum(v, CancellationToken.None), v, s));
            }

            foreach (var checksum in projectObject.AnalyzerReferences)
            {
                await VerifyAssetSerializationAsync<AnalyzerReference>(
                    service, checksum, WellKnownSynchronizationKinds.AnalyzerReference,
                    (v, k, s) => SolutionAsset.Create(s.CreateChecksum(v, CancellationToken.None), v, s));
            }

            foreach (var checksum in projectObject.AdditionalDocuments)
            {
                var documentObject = await service.GetValueAsync<DocumentStateChecksums>(checksum).ConfigureAwait(false);
                await VerifyAssetAsync(service, documentObject).ConfigureAwait(false);
            }
        }

        internal static async Task VerifyAssetAsync(ISolutionSynchronizationService service, DocumentStateChecksums documentObject)
        {
            var info = await VerifyAssetSerializationAsync<SerializedDocumentInfo>(
                service, documentObject.Info, WellKnownSynchronizationKinds.DocumentInfo,
                (v, k, s) => SolutionAsset.Create(s.CreateChecksum(v, CancellationToken.None), v, s)).ConfigureAwait(false);

            await VerifyAssetSerializationAsync<SourceText>(
                service, documentObject.Text, WellKnownSynchronizationKinds.SourceText,
                (v, k, s) => SolutionAsset.Create(s.CreateChecksum(v, CancellationToken.None), v, s));
        }

        internal static async Task<T> VerifyAssetSerializationAsync<T>(
            ISolutionSynchronizationService service,
            Checksum checksum,
            string kind,
            Func<T, string, Serializer, SynchronizationObject> assetGetter)
        {
            // re-create asset from object
            var syncService = (SolutionChecksumServiceFactory.Service)service;
            var syncObject = service.GetSynchronizationObject(checksum, CancellationToken.None);

            var recoveredValue = await service.GetValueAsync<T>(checksum);
            var recreatedSyncObject = assetGetter(recoveredValue, kind, syncService.Serializer_TestOnly);

            // make sure original object and re-created object are same.
            SynchronizationObjectEqual(syncObject, recreatedSyncObject);

            return recoveredValue;
        }

        internal static async Task VerifySolutionStateSerializationAsync(ISolutionSynchronizationService service, Solution solution, Checksum solutionChecksum)
        {
            var solutionObjectFromSyncObject = await service.GetValueAsync<SolutionStateChecksums>(solutionChecksum);

            SolutionStateChecksums solutionObjectFromSolution;
            Assert.True(solution.State.TryGetStateChecksums(out solutionObjectFromSolution));

            SolutionStateEqual(service, solutionObjectFromSolution, solutionObjectFromSyncObject);
        }

        internal static void SolutionStateEqual(ISolutionSynchronizationService service, SolutionStateChecksums solutionObject1, SolutionStateChecksums solutionObject2)
        {
            ChecksumWithChildrenEqual(solutionObject1, solutionObject2);

            ProjectStatesEqual(service, solutionObject1.Projects.ToProjectObjects(service), solutionObject2.Projects.ToProjectObjects(service));
        }

        internal static void ProjectStateEqual(ISolutionSynchronizationService service, ProjectStateChecksums projectObjects1, ProjectStateChecksums projectObjects2)
        {
            ChecksumWithChildrenEqual(projectObjects1, projectObjects2);

            ChecksumWithChildrenEqual(projectObjects1.Documents.ToDocumentObjects(service), projectObjects2.Documents.ToDocumentObjects(service));
            ChecksumWithChildrenEqual(projectObjects1.AdditionalDocuments.ToDocumentObjects(service), projectObjects2.AdditionalDocuments.ToDocumentObjects(service));
        }

        internal static void ProjectStatesEqual(ISolutionSynchronizationService service, ChecksumObjectCollection<ProjectStateChecksums> projectObjects1, ChecksumObjectCollection<ProjectStateChecksums> projectObjects2)
        {
            SynchronizationObjectEqual(projectObjects1, projectObjects2);

            Assert.Equal(projectObjects1.Count, projectObjects2.Count);

            for (var i = 0; i < projectObjects1.Count; i++)
            {
                ProjectStateEqual(service, projectObjects1[i], projectObjects2[i]);
            }
        }

        internal static void ChecksumWithChildrenEqual<T>(ChecksumObjectCollection<T> checksums1, ChecksumObjectCollection<T> checksums2) where T : ChecksumWithChildren
        {
            SynchronizationObjectEqual(checksums1, checksums2);

            Assert.Equal(checksums1.Count, checksums2.Count);

            for (var i = 0; i < checksums1.Count; i++)
            {
                ChecksumWithChildrenEqual(checksums1[i], checksums2[i]);
            }
        }

        internal static void ChecksumWithChildrenEqual(ChecksumWithChildren checksums1, ChecksumWithChildren checksums2)
        {
            Assert.Equal(checksums1.Checksum, checksums2.Checksum);
            Assert.Equal(checksums1.Children.Count, checksums2.Children.Count);

            for (var i = 0; i < checksums1.Children.Count; i++)
            {
                var child1 = checksums1.Children[i];
                var child2 = checksums2.Children[i];

                Assert.Equal(child1.GetType(), child2.GetType());

                if (child1 is Checksum)
                {
                    Assert.Equal((Checksum)child1, (Checksum)child2);
                    continue;
                }

                ChecksumWithChildrenEqual((ChecksumCollection)child1, (ChecksumCollection)child2);
            }
        }

        internal static void VerifySnapshotInService(
            ISolutionSynchronizationService snapshotService,
            ProjectStateChecksums projectObject,
            int expectedDocumentCount,
            int expectedProjectReferenceCount,
            int expectedMetadataReferenceCount,
            int expectedAnalyzerReferenceCount,
            int expectedAdditionalDocumentCount)
        {
            VerifyChecksumInService(snapshotService, projectObject.Checksum, projectObject.GetWellKnownSynchronizationKinds());
            VerifyChecksumInService(snapshotService, projectObject.Info, WellKnownSynchronizationKinds.ProjectInfo);
            VerifyChecksumInService(snapshotService, projectObject.CompilationOptions, WellKnownSynchronizationKinds.CompilationOptions);
            VerifyChecksumInService(snapshotService, projectObject.ParseOptions, WellKnownSynchronizationKinds.ParseOptions);

            VerifyCollectionInService(snapshotService, projectObject.Documents.ToDocumentObjects(snapshotService), expectedDocumentCount);

            VerifyCollectionInService(snapshotService, projectObject.ProjectReferences, expectedProjectReferenceCount, WellKnownSynchronizationKinds.ProjectReference);
            VerifyCollectionInService(snapshotService, projectObject.MetadataReferences, expectedMetadataReferenceCount, WellKnownSynchronizationKinds.MetadataReference);
            VerifyCollectionInService(snapshotService, projectObject.AnalyzerReferences, expectedAnalyzerReferenceCount, WellKnownSynchronizationKinds.AnalyzerReference);

            VerifyCollectionInService(snapshotService, projectObject.AdditionalDocuments.ToDocumentObjects(snapshotService), expectedAdditionalDocumentCount);
        }

        internal static void VerifyCollectionInService(ISolutionSynchronizationService snapshotService, ChecksumCollection checksums, int expectedCount, string expectedItemKind)
        {
            VerifyChecksumInService(snapshotService, checksums.Checksum, checksums.GetWellKnownSynchronizationKinds());
            Assert.Equal(checksums.Count, expectedCount);

            foreach (var checksum in checksums)
            {
                VerifyChecksumInService(snapshotService, checksum, expectedItemKind);
            }
        }

        internal static void VerifyCollectionInService(ISolutionSynchronizationService snapshotService, ChecksumObjectCollection<DocumentStateChecksums> documents, int expectedCount)
        {
            VerifySynchronizationObjectInService(snapshotService, documents);
            Assert.Equal(documents.Count, expectedCount);

            foreach (var documentId in documents)
            {
                VerifySnapshotInService(snapshotService, documentId);
            }
        }

        internal static void VerifySnapshotInService(ISolutionSynchronizationService snapshotService, DocumentStateChecksums documentObject)
        {
            VerifyChecksumInService(snapshotService, documentObject.Checksum, documentObject.GetWellKnownSynchronizationKinds());
            VerifyChecksumInService(snapshotService, documentObject.Info, WellKnownSynchronizationKinds.DocumentInfo);
            VerifyChecksumInService(snapshotService, documentObject.Text, WellKnownSynchronizationKinds.SourceText);
        }

        internal static void VerifySynchronizationObjectInService<T>(ISolutionSynchronizationService snapshotService, T syncObject) where T : SynchronizationObject
        {
            VerifyChecksumInService(snapshotService, syncObject.Checksum, syncObject.Kind);
        }

        internal static void VerifyChecksumInService(ISolutionSynchronizationService snapshotService, Checksum checksum, string kind)
        {
            Assert.NotNull(checksum);
            var otherObject = snapshotService.GetSynchronizationObject(checksum, CancellationToken.None);

            ChecksumEqual(checksum, kind, otherObject.Checksum, otherObject.Kind);
        }

        internal static void SynchronizationObjectEqual<T>(T checksumObject1, T checksumObject2) where T : SynchronizationObject
        {
            ChecksumEqual(checksumObject1.Checksum, checksumObject1.Kind, checksumObject2.Checksum, checksumObject2.Kind);
        }

        internal static void ChecksumEqual(Checksum checksum1, string kind1, Checksum checksum2, string kind2)
        {
            Assert.Equal(checksum1, checksum2);
            Assert.Equal(kind1, kind2);
        }

        private class TestAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
        {
            public void AddDependencyLocation(string fullPath)
            {
            }

            public Assembly LoadFromPath(string fullPath)
            {
                return Assembly.LoadFrom(fullPath);
            }
        }
    }
}