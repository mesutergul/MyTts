using System;
using System.Linq;
using System.Linq.Expressions;
using AutoMapper;
using IConfigurationProvider = AutoMapper.IConfigurationProvider;

namespace MyTts.Data
{
    public class NullMapper : IMapper
    {
        // Required by IMapper, but unused in this null implementation
        public IConfigurationProvider ConfigurationProvider { get; }

        public NullMapper()
        {
            // Create an “empty” configuration so ConfigurationProvider isn’t null
            ConfigurationProvider = new MapperConfiguration(cfg => { });
        }

        // Map<TDestination>(object source)
        public TDestination Map<TDestination>(object source)
        {
            if (source == null)
                return default!;

            // If source is already the destination type, just cast
            if (source is TDestination dst)
                return dst;

            // Fallback: attempt to create a new instance and copy matching properties by name
            var destination = Activator.CreateInstance<TDestination>();
            foreach (var prop in typeof(TDestination).GetProperties().Where(p => p.CanWrite))
            {
                var srcProp = source.GetType().GetProperty(prop.Name);
                if (srcProp?.CanRead == true && srcProp.PropertyType == prop.PropertyType)
                {
                    var val = srcProp.GetValue(source);
                    prop.SetValue(destination, val);
                }
            }
            return destination;
        }

        // Map<TSource, TDestination>(TSource source)
        public TDestination Map<TSource, TDestination>(TSource source)
            => Map<TDestination>(source! as object);

        // In-place map. We do nothing.
        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
            => destination;

        // ProjectTo isn’t supported by NullMapper
        public IQueryable<TDestination> ProjectTo<TDestination>(IQueryable source,
            object? parameters = null)
            => throw new NotSupportedException("ProjectTo is not supported by NullMapper.");

        // Other overloads simply forward to the above or throw
        public object Map(object source, Type sourceType, Type destinationType)
            => typeof(NullMapper)
                .GetMethod(nameof(Map), new[] { typeof(object) })!
                .MakeGenericMethod(destinationType)
                .Invoke(this, new[] { source })!;

        public TDestination DynamicMap<TDestination>(object source)
            => Map<TDestination>(source);

        TDestination IMapper.Map<TDestination>(object source, Action<IMappingOperationOptions<object, TDestination>> opts)
        {
            throw new NotImplementedException();
        }

        TDestination IMapper.Map<TSource, TDestination>(TSource source, Action<IMappingOperationOptions<TSource, TDestination>> opts)
        {
            throw new NotImplementedException();
        }

        TDestination IMapper.Map<TSource, TDestination>(TSource source, TDestination destination, Action<IMappingOperationOptions<TSource, TDestination>> opts)
        {
            throw new NotImplementedException();
        }

        object IMapper.Map(object source, Type sourceType, Type destinationType, Action<IMappingOperationOptions<object, object>> opts)
        {
            throw new NotImplementedException();
        }

        object IMapper.Map(object source, object destination, Type sourceType, Type destinationType, Action<IMappingOperationOptions<object, object>> opts)
        {
            throw new NotImplementedException();
        }

        IQueryable<TDestination> IMapper.ProjectTo<TDestination>(IQueryable source, object parameters, params Expression<Func<TDestination, object>>[] membersToExpand)
        {
            throw new NotImplementedException();
        }

        IQueryable<TDestination> IMapper.ProjectTo<TDestination>(IQueryable source, IDictionary<string, object> parameters, params string[] membersToExpand)
        {
            throw new NotImplementedException();
        }

        IQueryable IMapper.ProjectTo(IQueryable source, Type destinationType, IDictionary<string, object> parameters, params string[] membersToExpand)
        {
            throw new NotImplementedException();
        }

        object IMapperBase.Map(object source, object destination, Type sourceType, Type destinationType)
        {
            throw new NotImplementedException();
        }

        // If you need any of the other IMapper members, you can add no-op or throw implementations here...
    }
}