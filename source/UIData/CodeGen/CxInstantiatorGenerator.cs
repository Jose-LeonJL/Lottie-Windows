// Copyright(c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Toolkit.Uwp.UI.Lottie.WinCompData;
using Microsoft.Toolkit.Uwp.UI.Lottie.WinCompData.Mgcg;
using Microsoft.Toolkit.Uwp.UI.Lottie.WinUIXamlMediaData;

namespace Microsoft.Toolkit.Uwp.UI.Lottie.UIData.CodeGen
{
#if PUBLIC_UIData
    public
#endif
    sealed class CxInstantiatorGenerator : InstantiatorGeneratorBase
    {
        readonly CppStringifier _stringifier;
        readonly string _headerFileName;

        CxInstantiatorGenerator(
            string className,
            CompositionObject graphRoot,
            TimeSpan duration,
            bool setCommentProperties,
            bool disableFieldOptimization,
            CppStringifier stringifier,
            string headerFileName)
            : base(
                  className: className,
                  graphRoot: graphRoot,
                  duration: duration,
                  setCommentProperties: setCommentProperties,
                  disableFieldOptimization: false,
                  stringifier: stringifier)
        {
            _stringifier = stringifier;
            _headerFileName = headerFileName;
        }

        /// <summary>
        /// Returns the Cx code for a factory that will instantiate the given <see cref="Visual"/> as a
        /// Windows.UI.Composition Visual.
        /// </summary>
        /// <returns>A value tuple containing the cpp code, header code, and list of referenced asset files.</returns>
        public static (string cppText, string hText, IEnumerable<Uri> assetList) CreateFactoryCode(
            string className,
            Visual rootVisual,
            float width,
            float height,
            TimeSpan duration,
            string headerFileName,
            bool disableFieldOptimization)
        {
            var generator = new CxInstantiatorGenerator(
                className: className,
                graphRoot: rootVisual,
                duration: duration,
                disableFieldOptimization: disableFieldOptimization,
                setCommentProperties: false,
                stringifier: new CppStringifier(),
                headerFileName: headerFileName);

            var cppText = generator.GenerateCode(className, width, height);

            var hText = GenerateHeaderText(className);

            var assetList = generator.GetAssetsList();

            return (cppText, hText, assetList);
        }

        // Generates the .h file contents.
        static string GenerateHeaderText(string className)
        {
            return
$@"#pragma once
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace AnimatedVisuals 
{{
ref class {className} sealed : public Microsoft::UI::Xaml::Controls::IAnimatedVisualSource
{{
public:
    virtual Microsoft::UI::Xaml::Controls::IAnimatedVisual^ TryCreateAnimatedVisual(
        Windows::UI::Composition::Compositor^ compositor,
        Platform::Object^* diagnostics);
}};
}}";
        }

        /// <inheritdoc/>
        // Called by the base class to write the start of the file (i.e. everything up to the body of the Instantiator class).
        protected override void WriteFileStart(
            CodeBuilder builder,
            CodeGenInfo info,
            IEnumerable<ObjectData> nodes)
        {
            builder.WriteLine("#include \"pch.h\"");
            builder.WriteLine($"#include \"{_headerFileName}\"");

            // floatY, floatYxZ
            builder.WriteLine("#include \"WindowsNumerics.h\"");

            if (info.UsesCanvasEffects ||
                info.UsesCanvasGeometry)
            {
                // D2D
                builder.WriteLine("#include \"d2d1.h\"");
                builder.WriteLine("#include <d2d1_1.h>");
                builder.WriteLine("#include <d2d1helper.h>");

                // Interop
                builder.WriteLine("#include <Windows.Graphics.Interop.h>");

                // ComPtr
                builder.WriteLine("#include <wrl.h>");
            }

            if (info.UsesStreams)
            {
                builder.WriteLine("#include <iostream>");
            }

            if (info.UsesCanvasEffects ||
                info.UsesCanvas)
            {
                // throw an exception in this case for now. In the future the necessary
                // C++ code gen will be added
                throw new InvalidOperationException("Win2D dependency detected.");
            }

            builder.WriteLine();
            builder.WriteLine("using namespace Windows::Foundation;");
            builder.WriteLine("using namespace Windows::Foundation::Numerics;");
            builder.WriteLine("using namespace Windows::UI;");
            builder.WriteLine("using namespace Windows::UI::Composition;");
            builder.WriteLine("using namespace Windows::Graphics;");
            builder.WriteLine("using namespace Microsoft::WRL;");
            if (info.UsesNamespaceWindowsUIXamlMedia)
            {
                builder.WriteLine("using namespace Windows::UI::Xaml::Media;");
            }

            if (info.UsesStreams)
            {
                builder.WriteLine("using namespace Platform;");
                builder.WriteLine("using namespace Windows::Storage::Streams;");
            }

            builder.WriteLine();

            // Put the Instantiator class in an anonymous namespace.
            builder.WriteLine("namespace");
            builder.WriteLine("{");

            if (info.UsesCanvasEffects ||
                info.UsesCanvasGeometry)
            {
                // Write GeoSource to allow it's use in function definitions
                builder.WriteLine($"{_stringifier.GeoSourceClass}");

                // Typedef to simplify generation
                builder.WriteLine("typedef ComPtr<GeoSource> CanvasGeometry;");
            }
        }

        /// <inheritdoc/>
        protected override void WriteInstantiatorStart(CodeBuilder builder, CodeGenInfo info)
        {
            // Start writing the instantiator.
            builder.WriteLine("ref class AnimatedVisual sealed : public Microsoft::UI::Xaml::Controls::IAnimatedVisual");
            builder.OpenScope();

            if (info.UsesCanvasEffects ||
                info.UsesCanvasGeometry)
            {
                // D2D factory field.
                builder.WriteLine("ComPtr<ID2D1Factory> _d2dFactory;");
            }
        }

        /// <inheritdoc/>
        // Called by the base class to write the end of the file (i.e. everything after the body of the Instantiator class).
        protected override void WriteFileEnd(
            CodeBuilder builder,
            CodeGenInfo info,
            IEnumerable<ObjectData> nodes)
        {
            if (info.UsesCanvasEffects ||
                info.UsesCanvasGeometry)
            {
                // Utility method for D2D geometries
                builder.WriteLine("static IGeometrySource2D^ CanvasGeometryToIGeometrySource2D(CanvasGeometry geo)");
                builder.OpenScope();
                builder.WriteLine("ComPtr<ABI::Windows::Graphics::IGeometrySource2D> interop = geo.Detach();");
                builder.WriteLine("return reinterpret_cast<IGeometrySource2D^>(interop.Get());");
                builder.CloseScope();
                builder.WriteLine();

                // Utility method for fail-fasting on bad HRESULTs from d2d operations
                builder.WriteLine("static void FFHR(HRESULT hr)");
                builder.OpenScope();
                builder.WriteLine("if (hr != S_OK)");
                builder.OpenScope();
                builder.WriteLine("RoFailFastWithErrorContext(hr);");
                builder.CloseScope();
                builder.CloseScope();
                builder.WriteLine();
            }

            // Write the constructor for the instantiator.
            builder.UnIndent();
            builder.WriteLine("public:");
            builder.Indent();
            builder.WriteLine("AnimatedVisual(Compositor^ compositor)");

            // Initializer list.
            builder.Indent();
            builder.WriteLine(": _c(compositor)");

            // Instantiate the reusable ExpressionAnimation.
            builder.WriteLine($", {info.ReusableExpressionAnimationFieldName}(compositor->CreateExpressionAnimation())");
            builder.UnIndent();
            builder.OpenScope();
            if (info.UsesCanvasEffects ||
                info.UsesCanvasGeometry)
            {
                builder.WriteLine($"{FailFastWrapper("D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, _d2dFactory.GetAddressOf())")};");
            }

            // Instantiate the root. This will cause the whole Visual tree to be built and animations started.
            builder.WriteLine("Root();");
            builder.CloseScope();

            // Write the destructor. This is how CX implements IClosable/IDisposable.
            builder.WriteLine("virtual ~AnimatedVisual() { }");

            // Write the members on IAnimatedVisual.
            builder.WriteLine();
            builder.WriteLine("property TimeSpan Duration");
            builder.OpenScope();
            builder.WriteLine("virtual TimeSpan get() { return { c_durationTicks }; }");
            builder.CloseScope();
            builder.WriteLine();
            builder.WriteLine("property Visual^ RootVisual");
            builder.OpenScope();
            builder.WriteLine("virtual Visual^ get() { return _root; }");
            builder.CloseScope();
            builder.WriteLine();
            builder.WriteLine("property float2 Size");
            builder.OpenScope();
            builder.WriteLine($"virtual float2 get() {{ return {Vector2(info.CompositionDeclaredSize)}; }}");
            builder.CloseScope();
            builder.WriteLine();

            // Close the scope for the instantiator class.
            builder.UnIndent();
            builder.WriteLine("};");

            // Close the anonymous namespace.
            builder.WriteLine("} // end namespace");
            builder.WriteLine();

            // Generate the method that creates an instance of the composition.
            WriteTryCreateInstantiator(builder, info);
        }

        /// <summary>
        /// Generate the method that creates an instance of the composition.
        /// </summary>
        void WriteTryCreateInstantiator(CodeBuilder builder, CodeGenInfo info)
        {
            builder.WriteLine($"Microsoft::UI::Xaml::Controls::IAnimatedVisual^ AnimatedVisuals::{info.ClassName}::TryCreateAnimatedVisual(");
            builder.Indent();
            builder.WriteLine("Compositor^ compositor,");
            builder.WriteLine("Object^* diagnostics)");
            builder.UnIndent();
            builder.OpenScope();
            builder.WriteLine("diagnostics = nullptr;");
            builder.WriteLine("if (!IsRuntimeCompatible())");
            builder.OpenScope();
            builder.WriteLine("return nullptr;");
            builder.CloseScope();
            builder.WriteLine("return ref new AnimatedVisual(compositor);");
            builder.CloseScope();
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryCombinationFactory(CodeBuilder builder, CanvasGeometry.Combination obj, string typeName, string fieldName)
        {
            builder.WriteLine($"{typeName} result;");
            builder.WriteLine("ID2D1Geometry *geoA = nullptr, *geoB = nullptr;");
            builder.WriteLine($"{CallFactoryFor(obj.A)}->GetGeometry(&geoA);");
            builder.WriteLine($"{CallFactoryFor(obj.B)}->GetGeometry(&geoB);");
            builder.WriteLine("ComPtr<ID2D1PathGeometry> path;");
            builder.WriteLine($"{FailFastWrapper("_d2dFactory->CreatePathGeometry(&path)")};");
            builder.WriteLine("ComPtr<ID2D1GeometrySink> sink;");
            builder.WriteLine($"{FailFastWrapper("path->Open(&sink)")};");
            builder.WriteLine($"FFHR(geoA->CombineWithGeometry(");
            builder.Indent();
            builder.WriteLine($"geoB,");
            builder.WriteLine($"{_stringifier.CanvasGeometryCombine(obj.CombineMode)},");
            builder.WriteLine($"{_stringifier.Matrix3x2(obj.Matrix)},");
            builder.WriteLine($"sink.Get()));");
            builder.UnIndent();
            builder.WriteLine("geoA->Release();");
            builder.WriteLine("geoB->Release();");
            builder.WriteLine($"{FailFastWrapper("sink->Close()")};");
            builder.WriteLine($"result = {FieldAssignment(fieldName)}new GeoSource(path.Get());");
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryEllipseFactory(CodeBuilder builder, CanvasGeometry.Ellipse obj, string typeName, string fieldName)
        {
            builder.WriteLine($"{typeName} result;");
            builder.WriteLine("ComPtr<ID2D1EllipseGeometry> ellipse;");
            builder.WriteLine("FFHR(_d2dFactory->CreateEllipseGeometry(");
            builder.Indent();
            builder.WriteLine($"D2D1::Ellipse({{{Float(obj.X)},{Float(obj.Y)}}}, {Float(obj.RadiusX)}, {Float(obj.RadiusY)}),");
            builder.WriteLine("&ellipse));");
            builder.UnIndent();
            builder.WriteLine($"result = {FieldAssignment(fieldName)}new GeoSource(ellipse.Get());");
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryGroupFactory(CodeBuilder builder, CanvasGeometry.Group obj, string typeName, string fieldName)
        {
            builder.WriteLine($"ComPtr<ID2D1Geometry> geometries[{obj.Geometries.Length}];");
            builder.OpenScope();
            for (var i = 0; i < obj.Geometries.Length; i++)
            {
                var geometry = obj.Geometries[i];
                builder.WriteLine($"{CallFactoryFor(geometry)}->GetGeometry(&geometries[{i}]);");
            }

            builder.CloseScope();
            builder.WriteLine($"{typeName} result;");
            builder.WriteLine("ComPtr<ID2D1GeometryGroup> group;");
            builder.WriteLine("FFHR(_d2dFactory->CreateGeometryGroup(");
            builder.Indent();
            builder.WriteLine($"{FilledRegionDetermination(obj.FilledRegionDetermination)},");
            builder.WriteLine("geometries[0].GetAddressOf(),");
            builder.WriteLine($"{obj.Geometries.Length},");
            builder.WriteLine("&group));");
            builder.UnIndent();
            builder.WriteLine($"result = {FieldAssignment(fieldName)}new GeoSource(group.Get());");
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryPathFactory(CodeBuilder builder, CanvasGeometry.Path obj, string typeName, string fieldName)
        {
            builder.WriteLine($"{typeName} result;");

            // D2D Setup
            builder.WriteLine("ComPtr<ID2D1PathGeometry> path;");
            builder.WriteLine($"{FailFastWrapper("_d2dFactory->CreatePathGeometry(&path)")};");
            builder.WriteLine("ComPtr<ID2D1GeometrySink> sink;");
            builder.WriteLine($"{FailFastWrapper("path->Open(&sink)")};");

            if (obj.FilledRegionDetermination != CanvasFilledRegionDetermination.Alternate)
            {
                builder.WriteLine($"sink->SetFillMode({FilledRegionDetermination(obj.FilledRegionDetermination)});");
            }

            foreach (var command in obj.Commands)
            {
                switch (command.Type)
                {
                    case CanvasPathBuilder.CommandType.BeginFigure:
                        // Assume D2D1_FIGURE_BEGIN_FILLED
                        builder.WriteLine($"sink->BeginFigure({Vector2(((CanvasPathBuilder.Command.BeginFigure)command).StartPoint)}, D2D1_FIGURE_BEGIN_FILLED);");
                        break;
                    case CanvasPathBuilder.CommandType.EndFigure:
                        builder.WriteLine($"sink->EndFigure({CanvasFigureLoop(((CanvasPathBuilder.Command.EndFigure)command).FigureLoop)});");
                        break;
                    case CanvasPathBuilder.CommandType.AddLine:
                        builder.WriteLine($"sink->AddLine({Vector2(((CanvasPathBuilder.Command.AddLine)command).EndPoint)});");
                        break;
                    case CanvasPathBuilder.CommandType.AddCubicBezier:
                        var cb = (CanvasPathBuilder.Command.AddCubicBezier)command;
                        builder.WriteLine($"sink->AddBezier({{ {Vector2(cb.ControlPoint1)}, {Vector2(cb.ControlPoint2)}, {Vector2(cb.EndPoint)} }});");
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }

            builder.WriteLine($"{FailFastWrapper("sink->Close()")};");
            builder.WriteLine("GeoSource* rawResult = new GeoSource(path.Get());");
            builder.WriteLine($"result = {FieldAssignment(fieldName)}rawResult;");
            builder.WriteLine("rawResult->Release();");
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryRoundedRectangleFactory(CodeBuilder builder, CanvasGeometry.RoundedRectangle obj, string typeName, string fieldName)
        {
            builder.WriteLine($"{typeName} result;");
            builder.WriteLine("ComPtr<ID2D1RoundedRectangleGeometry> rect;");
            builder.WriteLine("FFHR(_d2dFactory->CreateRoundedRectangleGeometry(");
            builder.Indent();
            builder.WriteLine($"D2D1::RoundedRect({{{Float(obj.X)},{Float(obj.Y)}}}, {Float(obj.RadiusX)}, {Float(obj.RadiusY)}),");
            builder.WriteLine("&rect));");
            builder.UnIndent();
            builder.WriteLine($"result = {FieldAssignment(fieldName)}new GeoSource(rect.Get());");
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryTransformedGeometryFactory(CodeBuilder builder, CanvasGeometry.TransformedGeometry obj, string typeName, string fieldName)
        {
            builder.WriteLine($"{typeName} result;");
            builder.WriteLine("ID2D1Geometry *geoA = nullptr;");
            builder.WriteLine("ID2D1TransformedGeometry *transformed;");
            builder.WriteLine($"D2D1_MATRIX_3X2_F transformMatrix{_stringifier.Matrix3x2(obj.TransformMatrix)};");
            builder.WriteLine();
            builder.WriteLine($"{CallFactoryFor(obj.SourceGeometry)}->GetGeometry(&geoA);");
            builder.WriteLine("FFHR(_d2dFactory->CreateTransformedGeometry(geoA, transformMatrix, &transformed));");
            builder.WriteLine("geoA->Release();");
            builder.WriteLine($"result = {FieldAssignment(fieldName)}new GeoSource(transformed);");
        }

        /// <inheritdoc/>
        protected override void WriteLoadedImageSurfaceFactory(CodeBuilder builder, CodeGenInfo info, LoadedImageSurface obj, string fieldName, Uri imageUri)
        {
            switch (obj.Type)
            {
                case LoadedImageSurface.LoadedImageSurfaceType.FromStream:
                    builder.WriteLine("auto stream = ref new InMemoryRandomAccessStream();");
                    builder.WriteLine("auto dataWriter = ref new DataWriter(stream->GetOutputStreamAt(0));");
                    builder.WriteLine($"dataWriter->WriteBytes({fieldName});");
                    builder.WriteLine("dataWriter->StoreAsync();");
                    builder.WriteLine("dataWriter->FlushAsync();");
                    builder.WriteLine("stream->Seek(0);");
                    builder.WriteLine($"{_stringifier.Var} result = Windows::UI::Xaml::Media::LoadedImageSurface::StartLoadFromStream(stream);");
                    break;
                case LoadedImageSurface.LoadedImageSurfaceType.FromUri:
                    builder.WriteLine($"{_stringifier.Var} result = Windows::UI::Xaml::Media::LoadedImageSurface::StartLoadFromUri(ref new Uri(\"{imageUri}\"));");
                    break;
            }
        }

        string CanvasFigureLoop(CanvasFigureLoop value) => _stringifier.CanvasFigureLoop(value);

        static string FieldAssignment(string fieldName) => fieldName != null ? $"{fieldName} = " : string.Empty;

        string FilledRegionDetermination(CanvasFilledRegionDetermination value) => _stringifier.FilledRegionDetermination(value);

        string Float(float value) => _stringifier.Float(value);

        string FailFastWrapper(string value) => _stringifier.FailFastWrapper(value);

        string Vector2(Vector2 value) => _stringifier.Vector2(value);
    }
}
