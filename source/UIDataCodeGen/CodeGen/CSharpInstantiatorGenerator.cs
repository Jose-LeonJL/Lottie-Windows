// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.Toolkit.Uwp.UI.Lottie.WinCompData;
using Microsoft.Toolkit.Uwp.UI.Lottie.WinCompData.MetaData;
using Microsoft.Toolkit.Uwp.UI.Lottie.WinCompData.Mgcg;
using Microsoft.Toolkit.Uwp.UI.Lottie.WinUIXamlMediaData;
using Mgce = Microsoft.Toolkit.Uwp.UI.Lottie.WinCompData.Mgce;

namespace Microsoft.Toolkit.Uwp.UI.Lottie.UIData.CodeGen
{
#if PUBLIC_UIDataCodeGen
    public
#endif
    sealed class CSharpInstantiatorGenerator : InstantiatorGeneratorBase
    {
        readonly Stringifier _s;

        CSharpInstantiatorGenerator(
            CodegenConfiguration configuration,
            Stringifier stringifier)
            : base(
                  configuration,
                  setCommentProperties: false,
                  stringifier: stringifier)
        {
            _s = stringifier;
        }

        /// <summary>
        /// Returns the C# code for a factory that will instantiate the given <see cref="Visual"/> as a
        /// Windows.UI.Composition Visual.
        /// </summary>
        /// <returns>A tuple containing the C# code and list of referenced asset files.</returns>
        public static (string csText, IEnumerable<Uri> assetList) CreateFactoryCode(CodegenConfiguration configuration)
        {
            var generator = new CSharpInstantiatorGenerator(
                                configuration: configuration,
                                stringifier: new Stringifier());

            var csText = generator.GenerateCode();

            var assetList = generator.GetAssetsList();

            return (csText, assetList);
        }

        /// <inheritdoc/>
        // Called by the base class to write the start of the file (i.e. everything up to the body of the Instantiator class).
        protected override void WriteFileStart(CodeBuilder builder, IAnimatedVisualSourceInfo info)
        {
            // A sorted set to hold the namespaces that the generated code will use. The set is maintained in sorted order.
            var namespaces = new SortedSet<string>();

            if (info.UsesCanvas)
            {
                namespaces.Add("Microsoft.Graphics.Canvas");
            }

            if (info.UsesCanvasEffects)
            {
                namespaces.Add("Microsoft.Graphics.Canvas.Effects");
            }

            if (info.UsesCanvasGeometry)
            {
                namespaces.Add("Microsoft.Graphics.Canvas.Geometry");
            }

            if (info.UsesNamespaceWindowsUIXamlMedia)
            {
                namespaces.Add("Windows.UI.Xaml.Media");
                namespaces.Add("System.Runtime.InteropServices.WindowsRuntime");
                namespaces.Add("Windows.Foundation");
                namespaces.Add("System.ComponentModel");
            }

            if (info.GenerateDependencyObject)
            {
                namespaces.Add("Windows.UI.Xaml");
            }

            if (info.UsesStreams)
            {
                namespaces.Add("System.IO");
            }

            namespaces.Add("Microsoft.UI.Xaml.Controls");
            namespaces.Add("System");
            namespaces.Add("System.Numerics");
            namespaces.Add("Windows.UI");
            namespaces.Add("Windows.UI.Composition");

            // Write out each namespace using.
            foreach (var n in namespaces)
            {
                builder.WriteLine($"using {n};");
            }

            builder.WriteLine();

            builder.WriteLine($"namespace {info.Namespace}");
            builder.OpenScope();

            // Write a description of the source as comments.
            foreach (var line in GetSourceDescriptionLines())
            {
                builder.WriteComment(line);
            }

            // If the composition has LoadedImageSurface, write a class that implements the IDynamicAnimatedVisualSource interface.
            // Otherwise, implement the IAnimatedVisualSource interface.
            if (info.LoadedImageSurfaces.Count > 0)
            {
                WriteIDynamicAnimatedVisualSource(builder, info);
            }
            else
            {
                WriteIAnimatedVisualSource(builder, info);
            }

            builder.CloseScope();
            builder.WriteLine();
        }

        static string ExposedType(PropertyBinding binding)
            => binding.ExposedType switch
            {
                PropertySetValueType.Color => "Color",
                PropertySetValueType.Scalar => "double",
                PropertySetValueType.Vector2 => "Vector2",
                PropertySetValueType.Vector3 => "Vector3",
                PropertySetValueType.Vector4 => "Vector4",
                _ => throw new InvalidOperationException(),
            };

        static string ActualType(PropertyBinding binding)
            => binding.ExposedType switch
            {
                PropertySetValueType.Color => "Vector4",
                PropertySetValueType.Scalar => "float",
                PropertySetValueType.Vector2 => "Vector2",
                PropertySetValueType.Vector3 => "Vector3",
                PropertySetValueType.Vector4 => "Vector4",
                _ => throw new InvalidOperationException(),
            };

        void WriteThemeProperties(
            CodeBuilder builder,
            IAnimatedVisualSourceInfo info)
        {
            foreach (var prop in info.SourceMetadata.PropertyBindings)
            {
                if (info.GenerateDependencyObject)
                {
                    builder.WriteLine($"public {ExposedType(prop)} {prop.Name}");
                    builder.OpenScope();
                    builder.WriteLine($"get => ({ExposedType(prop)})GetValue({prop.Name}Property);");
                    builder.WriteLine($"set => SetValue({prop.Name}Property, value);");
                }
                else
                {
                    builder.WriteLine($"public {ExposedType(prop)} {prop.Name}");
                    builder.OpenScope();
                    builder.WriteLine($"get => _theme{prop.Name};");
                    builder.WriteLine("set");
                    builder.OpenScope();
                    builder.WriteLine($"_theme{prop.Name} = value;");
                    builder.WriteLine($"if ({info.ThemePropertiesFieldName} != null)");
                    builder.OpenScope();
                    WriteThemePropertyInitialization(builder, info.ThemePropertiesFieldName, prop);
                    builder.CloseScope();
                    builder.CloseScope();
                }

                builder.CloseScope();
                builder.WriteLine();
            }
        }

        // Writes the static dependency property fields and their initializers.
        void WriteDependencyPropertyFields(
            CodeBuilder builder,
            IAnimatedVisualSourceInfo info)
        {
            if (info.GenerateDependencyObject)
            {
                foreach (var prop in info.SourceMetadata.PropertyBindings)
                {
                    builder.WriteComment($"Dependency property for {prop.Name}.");
                    builder.WriteLine($"public static readonly DependencyProperty {prop.Name}Property =");
                    builder.Indent();
                    builder.WriteLine($"DependencyProperty.Register({String(prop.Name)}, typeof({ExposedType(prop)}), typeof({info.ClassName}),");
                    builder.Indent();
                    builder.WriteLine($"new PropertyMetadata({GetDefaultPropertyBindingValue(prop)}, On{prop.Name}Changed));");
                    builder.UnIndent();
                    builder.UnIndent();
                    builder.WriteLine();
                }
            }
        }

        // Writes the methods that handle dependency property changes.
        void WriteDependencyPropertyChangeHandlers(
            CodeBuilder builder,
            IAnimatedVisualSourceInfo info)
        {
            // Add the dependency property change handler methods.
            if (info.GenerateDependencyObject)
            {
                foreach (var prop in info.SourceMetadata.PropertyBindings)
                {
                    builder.WriteLine($"static void On{prop.Name}Changed(DependencyObject d, DependencyPropertyChangedEventArgs args)");
                    builder.OpenScope();
                    WriteThemePropertyInitialization(builder, $"(({info.ClassName})d)._themeProperties?", prop, $"({ExposedType(prop)})args.NewValue");
                    builder.CloseScope();
                    builder.WriteLine();
                }
            }
        }

        /// <summary>
        /// Writes a class that implements the IAnimatedVisualSource interface.
        /// </summary>
        void WriteIAnimatedVisualSource(CodeBuilder builder, IAnimatedVisualSourceInfo info)
        {
            if (info.GenerateDependencyObject)
            {
                builder.WriteLine($"sealed class {info.ClassName} : DependencyObject, IAnimatedVisualSource");
            }
            else
            {
                builder.WriteLine($"sealed class {info.ClassName} : IAnimatedVisualSource");
            }

            builder.OpenScope();

            // Add the methods and fields needed for theming.
            WriteThemeMethodsAndFields(builder, info);

            // Generate the method that creates an instance of the animated visual.
            builder.WriteLine("public IAnimatedVisual TryCreateAnimatedVisual(Compositor compositor, out object diagnostics)");
            builder.OpenScope();
            builder.WriteLine("diagnostics = null;");
            if (info.IsThemed)
            {
                builder.WriteLine("EnsureThemeProperties(compositor);");
            }

            builder.WriteLine();

            // Check the runtime version and instantiate the highest compatible IAnimatedVisual class.
            var animatedVisualInfos = info.AnimatedVisualInfos.OrderByDescending(avi => avi.RequiredUapVersion).ToArray();
            for (var i = 0; i < animatedVisualInfos.Length; i++)
            {
                var current = animatedVisualInfos[i];
                builder.WriteLine($"if ({current.ClassName}.IsRuntimeCompatible())");
                builder.OpenScope();
                WriteInstantiateAndReturnAnimatedVisual(builder, current);
                builder.CloseScope();
                builder.WriteLine();
            }

            builder.WriteLine("return null;");
        }

        void WriteThemeMethodsAndFields(CodeBuilder builder, IAnimatedVisualSourceInfo info)
        {
            if (info.IsThemed)
            {
                builder.WriteLine($"CompositionPropertySet {info.ThemePropertiesFieldName};");

                // Add fields for each of the theme properties.
                // Not needed if generating a DependencyObject - the values wil be stored in DependencyProperty's.
                if (!info.GenerateDependencyObject)
                {
                    foreach (var prop in info.SourceMetadata.PropertyBindings)
                    {
                        var defaultValue = GetDefaultPropertyBindingValue(prop);

                        WriteInitializedField(builder, ExposedType(prop), $"_theme{prop.Name}", _s.VariableInitialization(defaultValue));
                    }
                }

                builder.WriteLine();

                // Add the public static dependency property fields for each theme property.
                WriteDependencyPropertyFields(builder, info);

                // Add properties for each of the theme properties.
                WriteThemeProperties(builder, info);

                builder.WriteLine("public CompositionPropertySet GetThemeProperties(Compositor compositor)");
                builder.OpenScope();
                builder.WriteLine("return EnsureThemeProperties(compositor);");
                builder.CloseScope();
                builder.WriteLine();

                if (info.SourceMetadata.PropertyBindings.Any(pb => pb.ExposedType == PropertySetValueType.Color))
                {
                    // There's at least one themed color. They will need a helper method to convert to Vector4.
                    builder.WriteLine("static Vector4 ColorAsVector4(Color color) => new Vector4(color.R, color.G, color.B, color.A);");
                    builder.WriteLine();
                }

                // Add the dependency property change handler methods.
                WriteDependencyPropertyChangeHandlers(builder, info);

                // EnsureThemeProperties(...) method implementation.
                builder.WriteLine("CompositionPropertySet EnsureThemeProperties(Compositor compositor)");
                builder.OpenScope();
                builder.WriteLine($"if ({info.ThemePropertiesFieldName} is null)");
                builder.OpenScope();
                builder.WriteLine($"{info.ThemePropertiesFieldName} = compositor.CreatePropertySet();");

                // Initialize the values in the property set.
                foreach (var prop in info.SourceMetadata.PropertyBindings)
                {
                    WriteThemePropertyInitialization(builder, info.ThemePropertiesFieldName, prop, prop.Name);
                }

                builder.CloseScope();
                builder.WriteLine("return _themeProperties;");
                builder.CloseScope();
                builder.WriteLine();
            }
        }

        string GetDefaultPropertyBindingValue(PropertyBinding prop)
             => prop.ExposedType switch
             {
                 PropertySetValueType.Color => _s.Color((WinCompData.Wui.Color)prop.DefaultValue),

                 // Scalars are stored as floats, but exposed as doubles as XAML markup prefers doubles.
                 PropertySetValueType.Scalar => _s.Double((float)prop.DefaultValue),
                 PropertySetValueType.Vector2 => _s.Vector2((Vector2)prop.DefaultValue),
                 PropertySetValueType.Vector3 => _s.Vector3((Vector3)prop.DefaultValue),
                 PropertySetValueType.Vector4 => _s.Vector4((Vector4)prop.DefaultValue),
                 _ => throw new InvalidOperationException(),
             };

        /// <summary>
        /// Write a class that implements the IDynamicAnimatedVisualSource interface.
        /// </summary>
        void WriteIDynamicAnimatedVisualSource(CodeBuilder builder, IAnimatedVisualSourceInfo info)
        {
            if (info.GenerateDependencyObject)
            {
                builder.WriteLine($"sealed class {info.ClassName} : DependencyObject, IDynamicAnimatedVisualSource, INotifyPropertyChanged");
            }
            else
            {
                builder.WriteLine($"sealed class {info.ClassName} : IDynamicAnimatedVisualSource, INotifyPropertyChanged");
            }

            builder.OpenScope();

            // Declare variables.
            builder.WriteLine($"{_s.Const(_s.Int32TypeName)} c_loadedImageSurfaceCount = {info.LoadedImageSurfaces.Count};");
            builder.WriteLine($"{_s.Int32TypeName} _loadCompleteEventCount;");
            builder.WriteLine("bool _isAnimatedVisualSourceDynamic = true;");
            builder.WriteLine("bool _isTryCreateAnimatedVisualCalled;");
            builder.WriteLine("bool _isImageLoadingStarted;");
            builder.WriteLine("EventRegistrationTokenTable<TypedEventHandler<IDynamicAnimatedVisualSource, object>> _animatedVisualInvalidatedEventTokenTable;");

            // Declare the variables to hold the LoadedImageSurfaces.
            foreach (var n in info.LoadedImageSurfaces)
            {
                builder.WriteLine($"{_s.ReferenceTypeName(n.TypeName)} {n.FieldName};");
            }

            builder.WriteLine();

            // Add the methods and fields needed for theming.
            WriteThemeMethodsAndFields(builder, info);

            // Implement the INotifyPropertyChanged.PropertyChanged event.
            builder.WriteSummaryComment("This implementation of the INotifyPropertyChanged.PropertyChanged event is specific to C# and does not work on WinRT.");
            builder.WriteLine("public event PropertyChangedEventHandler PropertyChanged;");
            builder.WriteLine();

            // Implement the AnimatedVisualInvalidated event.
            WriteAnimatedVisualInvalidatedEvent(builder);

            // Define properties.
            builder.WriteSummaryComment("If this property is set to true, <see cref=\"TryCreateAnimatedVisual\"/> will return null until all images have loaded. When all images have loaded, <see cref=\"TryCreateAnimatedVisual\"/> will return the AnimatedVisual. To use, set it when declaring the AnimatedVisualSource. Once <see cref=\"TryCreateAnimatedVisual\"/> is called, changes made to this property will be ignored. Default value is true.");
            builder.WriteLine("public bool IsAnimatedVisualSourceDynamic");
            builder.OpenScope();
            builder.WriteLine("get { return _isAnimatedVisualSourceDynamic; }");
            builder.WriteLine("set");
            builder.OpenScope();
            builder.WriteLine("if (!_isTryCreateAnimatedVisualCalled && _isAnimatedVisualSourceDynamic != value)");
            builder.OpenScope();
            builder.WriteLine("_isAnimatedVisualSourceDynamic = value;");
            builder.WriteLine("NotifyPropertyChanged(nameof(IsAnimatedVisualSourceDynamic));");
            builder.CloseScope();
            builder.CloseScope();
            builder.CloseScope();
            builder.WriteLine();

            builder.WriteSummaryComment("Returns true if all images have loaded. To see if the images succeeded to load, see <see cref=\"ImageSuccessfulLoadingProgress\"/>.");
            builder.WriteLine("public bool IsImageLoadingCompleted { get; private set; }");
            builder.WriteLine();

            builder.WriteSummaryComment("Represents the progress of the image loading. Returns value between 0 and 1. 0 means none of the images finished loading. 1 means all images finished loading.");
            builder.WriteLine("public double ImageSuccessfulLoadingProgress { get; private set; }");
            builder.WriteLine();

            // Generate the method that creates an instance of the animated visual.
            builder.WriteLine("public IAnimatedVisual TryCreateAnimatedVisual(Compositor compositor, out object diagnostics)");
            builder.OpenScope();
            builder.WriteLine("_isTryCreateAnimatedVisualCalled = true;");
            builder.WriteLine("diagnostics = null;");
            builder.WriteLine();

            // Check whether the runtime will support the lowest UAP version required.
            var animatedVisualInfos = info.AnimatedVisualInfos.OrderByDescending(avi => avi.RequiredUapVersion).ToArray();
            builder.WriteLine($"if (!{animatedVisualInfos[^1].ClassName}.IsRuntimeCompatible())");
            builder.OpenScope();
            builder.WriteLine("return null;");
            builder.CloseScope();
            builder.WriteLine();
            builder.WriteLine("EnsureImageLoadingStarted();");
            builder.WriteLine();
            builder.WriteLine("if (_isAnimatedVisualSourceDynamic && _loadCompleteEventCount != c_loadedImageSurfaceCount)");
            builder.OpenScope();
            builder.WriteLine("return null;");
            builder.CloseScope();
            builder.WriteLine();

            // Check the runtime version and instantiate the highest compatible IAnimatedVisual class.
            for (var i = 0; i < animatedVisualInfos.Length; i++)
            {
                var current = animatedVisualInfos[i];
                var versionTestRequired = i < animatedVisualInfos.Length - 1;

                if (i > 0)
                {
                    builder.WriteLine();
                }

                if (versionTestRequired)
                {
                    builder.WriteLine($"if ({current.ClassName}.IsRuntimeCompatible())");
                    builder.OpenScope();
                }

                WriteInstantiateAndReturnAnimatedVisual(builder, current);

                if (versionTestRequired)
                {
                    builder.CloseScope();
                }
            }

            builder.CloseScope();
            builder.WriteLine();

            // Generate the method that load all the LoadedImageSurfaces.
            WriteEnsureImageLoadingStarted(builder, info);

            // Generate the method that handle the LoadCompleted event of the LoadedImageSurface objects.
            WriteHandleLoadCompleted(builder);

            // Generate the method that raise the PropertyChanged event.
            builder.WriteLine("void NotifyPropertyChanged(string name)");
            builder.OpenScope();
            builder.WriteLine("PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));");
            builder.CloseScope();
            builder.WriteLine();

            // Generate the method that get or create the EventRegistrationTokenTable.
            builder.WriteLine("EventRegistrationTokenTable<TypedEventHandler<IDynamicAnimatedVisualSource, object>> GetAnimatedVisualInvalidatedEventRegistrationTokenTable()");
            builder.OpenScope();
            builder.WriteLine("return EventRegistrationTokenTable<TypedEventHandler<IDynamicAnimatedVisualSource, object>>.GetOrCreateEventRegistrationTokenTable(ref _animatedVisualInvalidatedEventTokenTable);");
        }

        /// <inheritdoc/>
        protected override void WriteAnimatedVisualStart(CodeBuilder builder, IAnimatedVisualInfo info)
        {
            // Start the instantiator class.
            builder.WriteLine($"sealed class {info.ClassName} : IAnimatedVisual");
            builder.OpenScope();
        }

        IEnumerable<string> GetConstructorParameters(IAnimatedVisualInfo info)
        {
            yield return "Compositor compositor";

            if (info.AnimatedVisualSourceInfo.IsThemed)
            {
                yield return "CompositionPropertySet themeProperties";
            }

            foreach (var loadedImageSurfaceNode in info.LoadedImageSurfaceNodes)
            {
                yield return $"{_s.ReferenceTypeName(loadedImageSurfaceNode.TypeName)} {_s.CamelCase(loadedImageSurfaceNode.Name)}";
            }
        }

        /// <inheritdoc/>
        // Called by the base class to write the end of the AnimatedVisual class.
        protected override void WriteAnimatedVisualEnd(
            CodeBuilder builder,
            IAnimatedVisualInfo info)
        {
            // Write the constructor for the AnimatedVisual class.
            builder.WriteLine($"internal {info.ClassName}(");
            builder.Indent();
            builder.WriteCommaSeparatedLines(GetConstructorParameters(info));
            builder.WriteLine(")");
            builder.UnIndent();
            builder.OpenScope();

            // Copy constructor parameters into fields.
            builder.WriteLine("_c = compositor;");

            if (info.AnimatedVisualSourceInfo.IsThemed)
            {
                builder.WriteLine($"{info.AnimatedVisualSourceInfo.ThemePropertiesFieldName} = themeProperties;");
            }

            var loadedImageSurfaceNodes = info.LoadedImageSurfaceNodes;
            foreach (var n in loadedImageSurfaceNodes)
            {
                builder.WriteLine($"{n.FieldName} = {_s.CamelCase(n.Name)};");
            }

            builder.WriteLine($"{info.AnimatedVisualSourceInfo.ReusableExpressionAnimationFieldName} = compositor.CreateExpressionAnimation();");

            builder.WriteLine("Root();");
            builder.CloseScope();
            builder.WriteLine();

            // Write the IAnimatedVisual implementation.
            builder.WriteLine("Visual IAnimatedVisual.RootVisual => _root;");
            builder.WriteLine($"TimeSpan IAnimatedVisual.Duration => TimeSpan.FromTicks({info.AnimatedVisualSourceInfo.DurationTicksFieldName});");
            builder.WriteLine($"Vector2 IAnimatedVisual.Size => {Vector2(info.AnimatedVisualSourceInfo.CompositionDeclaredSize)};");
            builder.WriteLine("void IDisposable.Dispose() => _root?.Dispose();");
            builder.WriteLine();

            // Write the IsRuntimeCompatible static method.
            builder.WriteLine("internal static bool IsRuntimeCompatible()");
            builder.OpenScope();
            builder.WriteLine($"return Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent(\"Windows.Foundation.UniversalApiContract\", {info.RequiredUapVersion});");
            builder.CloseScope();

            // Close the scope for the instantiator class.
            builder.CloseScope();
        }

        /// <inheritdoc/>
        // Called by the base class to write the end of the file (i.e. everything after the body of the AnimatedVisual class).
        protected override void WriteFileEnd(
            CodeBuilder builder,
            IAnimatedVisualSourceInfo info)
        {
            // Close the scope for the IAnimatedVisualSource class.
            builder.CloseScope();

            // Close the scope for the namespace
            builder.CloseScope();
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryCombinationFactory(CodeBuilder builder, CanvasGeometry.Combination obj, string typeName, string fieldName)
        {
            builder.WriteLine($"var result = {FieldAssignment(fieldName)}{CallFactoryFor(obj.A)}.");
            builder.Indent();
            builder.WriteLine($"CombineWith({CallFactoryFor(obj.B)},");
            if (obj.Matrix.IsIdentity)
            {
                builder.WriteLine("Matrix3x2.Identity,");
            }
            else
            {
                builder.WriteLine($"{_s.Matrix3x2(obj.Matrix)},");
            }

            builder.WriteLine($"{_s.CanvasGeometryCombine(obj.CombineMode)});");
            builder.UnIndent();
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryEllipseFactory(CodeBuilder builder, CanvasGeometry.Ellipse obj, string typeName, string fieldName)
        {
            builder.WriteLine($"var result = {FieldAssignment(fieldName)}CanvasGeometry.CreateEllipse(");
            builder.Indent();
            builder.WriteLine($"null,");
            builder.WriteLine($"{Float(obj.X)}, {Float(obj.Y)}, {Float(obj.RadiusX)}, {Float(obj.RadiusY)});");
            builder.UnIndent();
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryGroupFactory(CodeBuilder builder, CanvasGeometry.Group obj, string typeName, string fieldName)
        {
            builder.WriteLine($"var result = {FieldAssignment(fieldName)}CanvasGeometry.CreateGroup(");
            builder.Indent();
            builder.WriteLine($"null,");
            builder.WriteLine($"new CanvasGeometry[] {{ {string.Join(", ", obj.Geometries.Select(g => CallFactoryFor(g))) } }},");
            builder.WriteLine($"{_s.FilledRegionDetermination(obj.FilledRegionDetermination)});");
            builder.UnIndent();
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryPathFactory(CodeBuilder builder, CanvasGeometry.Path obj, string typeName, string fieldName)
        {
            builder.WriteLine($"{typeName} result;");
            builder.WriteLine($"using (var builder = new CanvasPathBuilder(null))");
            builder.OpenScope();
            if (obj.FilledRegionDetermination != CanvasFilledRegionDetermination.Alternate)
            {
                builder.WriteLine($"builder.SetFilledRegionDetermination({_s.FilledRegionDetermination(obj.FilledRegionDetermination)});");
            }

            foreach (var command in obj.Commands)
            {
                switch (command.Type)
                {
                    case CanvasPathBuilder.CommandType.BeginFigure:
                        builder.WriteLine($"builder.BeginFigure({Vector2(((CanvasPathBuilder.Command.BeginFigure)command).StartPoint)});");
                        break;
                    case CanvasPathBuilder.CommandType.EndFigure:
                        builder.WriteLine($"builder.EndFigure({_s.CanvasFigureLoop(((CanvasPathBuilder.Command.EndFigure)command).FigureLoop)});");
                        break;
                    case CanvasPathBuilder.CommandType.AddLine:
                        builder.WriteLine($"builder.AddLine({Vector2(((CanvasPathBuilder.Command.AddLine)command).EndPoint)});");
                        break;
                    case CanvasPathBuilder.CommandType.AddCubicBezier:
                        var cb = (CanvasPathBuilder.Command.AddCubicBezier)command;
                        builder.WriteLine($"builder.AddCubicBezier({Vector2(cb.ControlPoint1)}, {Vector2(cb.ControlPoint2)}, {Vector2(cb.EndPoint)});");
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }

            builder.WriteLine($"result = {FieldAssignment(fieldName)}CanvasGeometry.CreatePath(builder);");
            builder.CloseScope();
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryRoundedRectangleFactory(CodeBuilder builder, CanvasGeometry.RoundedRectangle obj, string typeName, string fieldName)
        {
            builder.WriteLine($"var result = {FieldAssignment(fieldName)}CanvasGeometry.CreateRoundedRectangle(");
            builder.Indent();
            builder.WriteLine("null,");
            builder.WriteLine($"{Float(obj.X)},");
            builder.WriteLine($"{Float(obj.Y)},");
            builder.WriteLine($"{Float(obj.W)},");
            builder.WriteLine($"{Float(obj.H)},");
            builder.WriteLine($"{Float(obj.RadiusX)},");
            builder.WriteLine($"{Float(obj.RadiusY)});");
            builder.UnIndent();
        }

        /// <inheritdoc/>
        protected override void WriteCanvasGeometryTransformedGeometryFactory(CodeBuilder builder, CanvasGeometry.TransformedGeometry obj, string typeName, string fieldName)
        {
            builder.WriteLine($"var result = {FieldAssignment(fieldName)}{CallFactoryFor(obj.SourceGeometry)}.Transform({_s.Matrix3x2(obj.TransformMatrix)});");
        }

        /// <inheritdoc/>
        protected override string WriteCompositeEffectFactory(CodeBuilder builder, Mgce.CompositeEffect compositeEffect)
        {
            var compositeEffectString = "compositeEffect";
            builder.WriteLine($"var {compositeEffectString} = new CompositeEffect();");
            builder.WriteLine($"{compositeEffectString}.Mode = {_s.CanvasCompositeMode(compositeEffect.Mode)};");
            foreach (var source in compositeEffect.Sources)
            {
                builder.WriteLine($"{compositeEffectString}.Sources.Add(new CompositionEffectSourceParameter({String(source.Name)}));");
            }

            return compositeEffectString;
        }

        void WriteAnimatedVisualInvalidatedEvent(CodeBuilder builder)
        {
            builder.WriteLine("public event TypedEventHandler<IDynamicAnimatedVisualSource, object> AnimatedVisualInvalidated");
            builder.OpenScope();
            builder.WriteLine("add");
            builder.OpenScope();
            builder.WriteLine("return GetAnimatedVisualInvalidatedEventRegistrationTokenTable().AddEventHandler(value);");
            builder.CloseScope();
            builder.WriteLine("remove");
            builder.OpenScope();
            builder.WriteLine("GetAnimatedVisualInvalidatedEventRegistrationTokenTable().RemoveEventHandler(value);");
            builder.CloseScope();
            builder.CloseScope();
            builder.WriteLine();
        }

        void WriteEnsureImageLoadingStarted(CodeBuilder builder, IAnimatedVisualSourceInfo info)
        {
            builder.WriteLine("void EnsureImageLoadingStarted()");
            builder.OpenScope();
            builder.WriteLine("if (!_isImageLoadingStarted)");
            builder.OpenScope();
            builder.WriteLine("var eventHandler = new TypedEventHandler<LoadedImageSurface, LoadedImageSourceLoadCompletedEventArgs>(HandleLoadCompleted);");

            foreach (var n in info.LoadedImageSurfaces)
            {
                switch (n.LoadedImageSurfaceType)
                {
                    case LoadedImageSurface.LoadedImageSurfaceType.FromStream:
                        builder.WriteLine($"{n.FieldName} = LoadedImageSurface.StartLoadFromStream({n.BytesFieldName}.AsBuffer().AsStream().AsRandomAccessStream());");
                        break;
                    case LoadedImageSurface.LoadedImageSurfaceType.FromUri:
                        builder.WriteLine($"{n.FieldName} = LoadedImageSurface.StartLoadFromUri(new Uri(\"{n.ImageUri}\"));");
                        break;
                    default:
                        throw new InvalidOperationException();
                }

                builder.WriteLine($"{n.FieldName}.LoadCompleted += eventHandler;");
            }

            builder.WriteLine("_isImageLoadingStarted = true;");
            builder.CloseScope();
            builder.CloseScope();
            builder.WriteLine();
        }

        void WriteHandleLoadCompleted(CodeBuilder builder)
        {
            builder.WriteLine("void HandleLoadCompleted(LoadedImageSurface sender, LoadedImageSourceLoadCompletedEventArgs e)");
            builder.OpenScope();
            builder.WriteLine("_loadCompleteEventCount++;");
            builder.WriteLine("sender.LoadCompleted -= HandleLoadCompleted;");
            builder.WriteLine();
            builder.WriteLine("if (e.Status == LoadedImageSourceLoadStatus.Success)");
            builder.OpenScope();
            builder.WriteLine("if (_isAnimatedVisualSourceDynamic && _loadCompleteEventCount == c_loadedImageSurfaceCount)");
            builder.OpenScope();
            builder.WriteLine("_animatedVisualInvalidatedEventTokenTable?.InvocationList?.Invoke(this, null);");
            builder.CloseScope();
            builder.WriteLine("ImageSuccessfulLoadingProgress = (double)_loadCompleteEventCount / c_loadedImageSurfaceCount;");
            builder.WriteLine("NotifyPropertyChanged(nameof(ImageSuccessfulLoadingProgress));");
            builder.CloseScope();
            builder.WriteLine();
            builder.WriteLine("if (_loadCompleteEventCount == c_loadedImageSurfaceCount)");
            builder.OpenScope();
            builder.WriteLine("IsImageLoadingCompleted = true;");
            builder.WriteLine("NotifyPropertyChanged(nameof(IsImageLoadingCompleted));");
            builder.CloseScope();
            builder.CloseScope();
            builder.WriteLine();
        }

        IEnumerable<string> GetConstructorArguments(IAnimatedVisualInfo info)
        {
            yield return "compositor";

            if (info.AnimatedVisualSourceInfo.IsThemed)
            {
                yield return info.AnimatedVisualSourceInfo.ThemePropertiesFieldName;
            }

            foreach (var loadedImageSurfaceNode in info.LoadedImageSurfaceNodes)
            {
                yield return loadedImageSurfaceNode.FieldName;
            }
        }

        void WriteInstantiateAndReturnAnimatedVisual(CodeBuilder builder, IAnimatedVisualInfo info)
        {
            builder.WriteLine("return");
            builder.Indent();
            builder.WriteLine($"new {info.ClassName}(");
            builder.Indent();
            builder.WriteCommaSeparatedLines(GetConstructorArguments(info));
            builder.WriteLine(");");
            builder.UnIndent();
            builder.UnIndent();
        }

        static string FieldAssignment(string fieldName) => fieldName != null ? $"{fieldName} = " : string.Empty;

        string Float(float value) => _s.Float(value);

        string Vector2(Vector2 value) => _s.Vector2(value);

        string String(string value) => _s.String(value);
    }
}
