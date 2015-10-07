using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rhino.Mocks;

namespace Test.Helpers
{
    public abstract class BuilderConfig
    {
        protected static Func<Type, object[], object> _generateStub = MockRepository.GenerateStub;
        protected static Func<Type, object[], object> _generateMock = GenerateRhinoMock;
        protected static Func<Type, object[], object> _generateStrictMock = GenerateRhinoStrictMock;
        protected static Func<Type, object[], object> _generatePartialMock = GenerateRhinoPartialMock;
        protected static Action<TransactionFactory, Func<Transaction>> _stubGetTransaction = (transactionFactory, getTransaction) =>
        {
            transactionFactory.Stub(x => x.Create()).Return(getTransaction());
        };

        public static void RegisterStubFactory(Func<Type, object[], object> method)
        {
            _generateStub = method;
        }

        public static void RegisterMockFactory(Func<Type, object[], object> method)
        {
            _generateMock = method;
        }

        public static void RegisterStrictMockFactory(Func<Type, object[], object> method)
        {
            _generateStrictMock = method;
        }

        public static void RegisterPartialMockFactory(Func<Type, object[], object> method)
        {
            _generatePartialMock = method;
        }

        public static void RegisterGetTransactionStubSetter(Action<TransactionFactory, Func<Transaction>> method)
        {
            _stubGetTransaction = method;
        }

        private static object GenerateRhinoMock(Type type, object[] parameters)
        {
            var mockRepository = new MockRepository();
            var mock = mockRepository.DynamicMock(type, parameters);
            mockRepository.Replay(mock);
            return mock;
        }

        private static object GenerateRhinoStrictMock(Type type, object[] parameters)
        {
            var mockRepository = new MockRepository();
            var mock = mockRepository.StrictMock(type, parameters);
            mockRepository.Replay(mock);
            return mock;
        }

        private static object GenerateRhinoPartialMock(Type type, object[] parameters)
        {
            var mockRepository = new MockRepository();
            var mock = mockRepository.PartialMock(type, parameters);
            mockRepository.Replay(mock);
            return mock;
        }
    }
    
    public abstract class BuilderBase<TTarget, TBuilder>
        : BuilderConfig
        where TTarget : class
        where TBuilder : BuilderBase<TTarget, TBuilder>
    {
        private readonly Dictionary<Type, object> _dependencies = new Dictionary<Type, object>();
        private bool _isBuilt;

        public TBuilder With<TDependency>(TDependency dependency)
        {
            if (_dependencies.ContainsKey(typeof (TDependency)))
            {
                throw new InvalidOperationException(
                    string.Format("A dependency of type {0} has already been registered.", typeof(TDependency).Name));
            }
            _dependencies[typeof(TDependency)] = dependency;
            return (TBuilder)this;
        }

        public TTarget Build(bool deepResolution = false)
        {
            return Build(Activator.CreateInstance, deepResolution);
        }

        public TTarget BuildMock(bool deepResolution = false)
        {
            return Build(_generateMock, deepResolution);
        }

        public TTarget BuildPartialMock(bool deepResolution = false, Func<object[], TTarget> generatePartialMock = null)
        {
            return Build((_, parameterValues) => generatePartialMock(parameterValues), deepResolution);
        }

        private TTarget Build(Func<Type, object[], object> generateInstance, bool deepResolution = false)
        {
            if (_isBuilt)
            {
                throw new InvalidOperationException("Target may only be built once per builder instance.");
            }
            try
            {
                _isBuilt = true;
                var constructor = FindConstructor(typeof(TTarget), true);
                var parameterTypes = constructor.GetParameters()
                    .Select(pi => pi.ParameterType)
                    .ToList();
                BeforeDependencyResolution(_dependencies, parameterTypes);
                var parameterTypesAndValues = FillDependencies(parameterTypes, deepResolution);
                BeforeBuild(_dependencies, parameterTypesAndValues);
                var parameterValues = parameterTypes
                    .Join(parameterTypesAndValues, pt => pt, ptv => ptv.Key, (_, ptv) => ptv.Value)
                    .ToArray();
                return generateInstance(typeof(TTarget), parameterValues) as TTarget;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(string.Format("Failed to build instance of type {0}.", typeof(TTarget).Name), ex);
            }
        }

        protected virtual void BeforeDependencyResolution(Dictionary<Type, object> dependencies, IEnumerable<Type> constructorParameterTypes)
        {
        }

        protected virtual void BeforeBuild(Dictionary<Type, object> dependencies, Dictionary<Type, object> parameterValues)
        {
        }

        private Dictionary<Type, object> FillDependencies(IEnumerable<Type> parameterTypes, bool deepResolution)
        {
            return parameterTypes
                .Aggregate(new Dictionary<Type, object>(),
                    (d, type) =>
                    {
                        d[type] = _dependencies.ContainsKey(type)
                            ? _dependencies[type]
                            : CreateDependency(type, _generateStub, deepResolution);
                        return d;
                    });
        }

        private static ConstructorInfo FindConstructor(Type type, bool mostParameters)
        {
            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(ci => ci.IsPublic || ci.IsFamily || ci.IsFamilyOrAssembly);
            var orderedConstructors = mostParameters
                ? constructors.OrderByDescending(ci => ci.GetParameters().Count())
                : constructors.OrderBy(ci => ci.GetParameters().Count());
            return orderedConstructors.First();   
        }

        protected static object CreateDependency(Type type, Func<Type, object[], object> generateFake, bool mostParameters = false)
        {
            try
            {
                if (type.IsValueType)
                {
                    return Activator.CreateInstance(type);
                }
                if (type == typeof (string))
                {
                    return string.Empty;
                }
                if (type.IsInterface)
                {
                    return generateFake(type, new object[0]);
                }
                var constructor = FindConstructor(type, mostParameters);
                var parameterValues = constructor.GetParameters()
                    .Select(pi => CreateDependency(pi.ParameterType, generateFake, mostParameters))
                    .ToArray();
                return generateFake(type, parameterValues);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(string.Format("Failed to create dependency stub for type {0}", type.Name), ex);
            }
        }
    }

    public class Builder<TTarget> : BuilderBase<TTarget, Builder<TTarget>> where TTarget : class
    {
        public static TTarget GenerateStub(bool mostParameters = false)
        {
            return CreateDependency(typeof(TTarget), _generateStub, mostParameters) as TTarget;
        }

        public static TTarget GenerateMock(bool mostParameters = false)
        {
            return CreateDependency(typeof(TTarget), _generateMock, mostParameters) as TTarget;
        }

        public static TTarget GenerateStrictMock(bool mostParameters = false)
        {
            return CreateDependency(typeof(TTarget), _generateStrictMock, mostParameters) as TTarget;
        }
    }
}