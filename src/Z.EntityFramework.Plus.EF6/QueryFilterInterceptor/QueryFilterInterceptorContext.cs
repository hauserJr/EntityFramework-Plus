﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Core.Metadata.Edm;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Reflection;

namespace Z.EntityFramework.Plus
{
    public class QueryFilterContextInterceptor
    {
        /// <summary>true if clear cache required.</summary>
        public bool ClearCacheRequired;

        /// <summary>The filter by entity set base.</summary>
        public Dictionary<string, List<BaseQueryFilterInterceptor>> FilterByEntitySetBase = new Dictionary<string, List<BaseQueryFilterInterceptor>>();

        /// <summary>Gets or sets the filters.</summary>
        /// <value>The filters.</value>
        public ConcurrentDictionary<object, BaseQueryFilterInterceptor> FilterByKey = new ConcurrentDictionary<object, BaseQueryFilterInterceptor>();

        /// <summary>Type of the filter by.</summary>
        public ConcurrentDictionary<Type, List<BaseQueryFilterInterceptor>> FilterByType = new ConcurrentDictionary<Type, List<BaseQueryFilterInterceptor>>();

        /// <summary>Gets or sets the filters.</summary>
        /// <value>The filters.</value>
        public ConcurrentDictionary<object, BaseQueryFilterInterceptor> GlobalFilterByKey = new ConcurrentDictionary<object, BaseQueryFilterInterceptor>();

        /// <summary>Type of the filter by.</summary>
        public ConcurrentDictionary<Type, List<BaseQueryFilterInterceptor>> GlobalFilterByType = new ConcurrentDictionary<Type, List<BaseQueryFilterInterceptor>>();

        /// <summary>Set the type by database belongs to.</summary>
        public Dictionary<string, List<Type>> TypeByDbSet = new Dictionary<string, List<Type>>();

        /// <summary>The type by entity set base.</summary>
        public Dictionary<string, Type> TypeByEntitySetBase = new Dictionary<string, Type>();

        public QueryFilterContextInterceptor(DbContext context)
        {
            Context = context;
            Initialize(context);
        }

        /// <summary>Gets or sets the context associated with the filter context.</summary>
        /// <value>The context associated with the filter context.</value>
        public DbContext Context { get; set; }

        /// <summary>Gets applicable filter.</summary>
        /// <param name="dbSetName">Name of the database set.</param>
        /// <returns>The applicable filter.</returns>
        public List<BaseQueryFilterInterceptor> GetApplicableFilter(string dbSetName)
        {
            var list = new List<BaseQueryFilterInterceptor>();

            var types = TypeByDbSet[dbSetName];

            foreach (var type in types)
            {
                List<BaseQueryFilterInterceptor> filterList;
                if (FilterByType.TryGetValue(type, out filterList))
                {
                    list.AddRange(filterList);
                }
            }

            return list;
        }

        /// <summary>Gets global applicable filter.</summary>
        /// <param name="dbSetName">Name of the database set.</param>
        /// <returns>The global applicable filter.</returns>
        public List<BaseQueryFilterInterceptor> GetGlobalApplicableFilter(string dbSetName)
        {
            var list = new List<BaseQueryFilterInterceptor>();

            var types = TypeByDbSet[dbSetName];

            foreach (var type in types)
            {
                List<BaseQueryFilterInterceptor> filterList;
                if (GlobalFilterByType.TryGetValue(type, out filterList))
                {
                    list.AddRange(filterList);
                }
            }

            return list;
        }

        /// <summary>Adds a query filter to the filter context associated with the specified key.</summary>
        /// <typeparam name="T">The type of elements of the query.</typeparam>
        /// <param name="key">The filter key.</param>
        /// <param name="filter">The filter.</param>
        /// <returns>The query filter added to the filter context associated with the specified ke .</returns>
        public BaseQueryFilterInterceptor AddFilter<T>(object key, Func<IQueryable<T>, IQueryable<T>> filter) where T : class
        {
            var queryFilter = new QueryFilterInterceptor<T>(filter);
            queryFilter.OwnerContext = this;

            // FilterByKey
            {
                FilterByKey.AddOrUpdate(key, queryFilter, (o, interceptorFilter) => queryFilter);
            }

            // FilterByType
            {
                if (!FilterByType.ContainsKey(typeof (T)))
                {
                    FilterByType.TryAdd(typeof (T), new List<BaseQueryFilterInterceptor>());
                }

                FilterByType[typeof (T)].Add(queryFilter);
            }

            ClearCache();
            return queryFilter;
        }

        /// <summary>Clears the cache.</summary>
        public void ClearCache()
        {
            if (ClearCacheRequired)
            {
                QueryFilterManager.ClearQueryCache(Context);
                ClearCacheRequired = false;
            }
        }

        /// <summary>Gets the filter associated to the specified key.</summary>
        /// <param name="key">The filter key.</param>
        /// <returns>The filter associated to the specified key.</returns>
        public BaseQueryFilterInterceptor GetFilter(object key)
        {
            BaseQueryFilterInterceptor filter;
            if (!FilterByKey.TryGetValue(key, out filter))
            {
                GlobalFilterByKey.TryGetValue(key, out filter);
            }
            return filter;
        }

        /// <summary>Initializes this object.</summary>
        /// <param name="context">The context.</param>
        private void Initialize(DbContext context)
        {
            // GET DbSet<> properties
            var setProperties = context.GetDbSetProperties();

            foreach (var setProperty in setProperties)
            {
                // GET DbSet<>
                var dbSet = (IQueryable) setProperty.GetValue(context, null);

                // DbSet<>.InternalQuery
                var internalQueryProperty = typeof (DbQuery<>).MakeGenericType(dbSet.ElementType).GetProperty("InternalQuery", BindingFlags.NonPublic | BindingFlags.Instance);
                var internalQuery = internalQueryProperty.GetValue(dbSet, null);

                // DbSet<>.InternalQuery.EntitySet
                var entitySetProperty = internalQuery.GetType().GetProperty("EntitySet", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var entitySet = (EntitySet) entitySetProperty.GetValue(internalQuery, null);

                var elementType = dbSet.ElementType;
                var entityTypebase = entitySet.ElementType.FullName;

                // TypeByEntitySetBase
                {
                    if (!TypeByEntitySetBase.ContainsKey(entityTypebase))
                    {
                        TypeByEntitySetBase.Add(entityTypebase, elementType);
                    }
                }

                // TypeByDbSet
                {
                    var baseType = elementType;

                    var types = new List<Type>();
                    while (baseType != null && baseType != typeof (object))
                    {
                        types.Add(baseType);

                        // LINK interface
                        var interfaces = baseType.GetInterfaces();
                        foreach (var @interface in interfaces)
                        {
                            types.Add(@interface);
                        }

                        baseType = baseType.BaseType;
                    }

                    // ENSURE all discting
                    types = types.Distinct().ToList();

                    if (!TypeByDbSet.ContainsKey(entityTypebase))
                    {
                        TypeByDbSet.Add(entityTypebase, types);
                    }
                }
            }
        }
    }
}