﻿using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using Newtonsoft.Json;
using NServiceBus.MessageInterfaces.MessageMapper.Reflection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{

    class TestableRepository<TEntity, TState, TParent> : TestableRepository<TEntity, TState>, IRepository<TEntity, TParent>, IRepositoryTest<TEntity, TParent> where TParent : IEntity where TEntity : Entity<TEntity, TState, TParent> where TState : class, IState, new()
    {
        private readonly TParent _parent;

        public TestableRepository(TParent parent, TestableUnitOfWork uow)
            : base(uow)
        {
            _parent = parent;
        }

        public override async Task<TEntity> TryGet(Id id)
        {
            if (id == null) return default(TEntity);
            try
            {
                return await Get(id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return default(TEntity);

        }
        public override async Task<TEntity> Get(Id id)
        {
            var cacheId = $"{_parent.Bucket}.{_parent.BuildParentsString()}.{id}";
            TEntity root;
            if (!Tracked.TryGetValue(cacheId, out root))
            {
                root = await GetUntracked(_parent.Bucket, id, _parent.BuildParents()).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }

            return root;
        }

        public override async Task<TEntity> New(Id id)
        {
            var cacheId = $"{_parent.Bucket}.{_parent.BuildParentsString()}.{id}";

            TEntity root;
            if (!Tracked.TryGetValue(cacheId, out root))
            {
                root = await NewUntracked(_parent.Bucket, id, _parent.BuildParents()).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }

            return root;
        }
        public override IEventPlanner Plan(Id id)
        {
            return new EventPlanner<TState>(_eventstore, _snapstore, _factory, _parent.Bucket, id, _parent.BuildParents());
        }

        protected override async Task<TEntity> GetUntracked(string bucket, Id id, Id[] parents)
        {
            var entity = await base.GetUntracked(bucket, id, parents).ConfigureAwait(false);

            entity.Parent = _parent;

            return entity;
        }

        protected override async Task<TEntity> NewUntracked(string bucket, Id id, Id[] parents)
        {
            var entity = await base.NewUntracked(bucket, id, parents).ConfigureAwait(false);

            entity.Parent = _parent;

            return entity;
        }
    }
    class TestableRepository<TEntity, TState> : IRepository<TEntity>, IRepositoryTest<TEntity> where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
    {
        private static readonly IEntityFactory<TEntity> Factory = EntityFactory.For<TEntity>();

        protected readonly ConcurrentDictionary<string, TEntity> Tracked = new ConcurrentDictionary<string, TEntity>();
        protected readonly TestableUnitOfWork _uow;
        protected readonly TestableEventFactory _factory;
        protected readonly TestableOobWriter _oobStore;
        protected readonly TestableEventStore _eventstore;
        protected readonly TestableSnapshotStore _snapstore;
        private bool _disposed;

        public TestableRepository(TestableUnitOfWork uow)
        {
            _uow = uow;
            _factory = new TestableEventFactory(new MessageMapper());
            _oobStore = new TestableOobWriter();
            _eventstore = new TestableEventStore();
            _snapstore = new TestableSnapshotStore();
        }

        public int ChangedStreams => Tracked.Count(x => x.Value.Dirty);

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            Tracked.Clear();

            _disposed = true;
        }

        public virtual Task<TEntity> Get(Id id)
        {
            return Get(Defaults.Bucket, id);
        }

        public async Task<TEntity> Get(string bucket, Id id)
        {
            var cacheId = $"{bucket}.{id}";
            TEntity root;
            if (!Tracked.TryGetValue(cacheId, out root))
            {
                root = await GetUntracked(bucket, id).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }

            return root;
        }
        protected virtual async Task<TEntity> GetUntracked(string bucket, Id id, Id[] parents = null)
        {
            var snapshot = await _snapstore.GetSnapshot<TEntity>(bucket, id, parents).ConfigureAwait(false);
            var events = await _eventstore.GetEvents<TEntity>(bucket, id, parents, start: snapshot?.Version).ConfigureAwait(false);

            var entity = Factory.Create(bucket, id, parents, events.Select(x => x.Event as Messages.IEvent).ToArray(), snapshot?.Payload);

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedStore).OobWriter = _oobStore;

            return entity;
        }

        public virtual Task<TEntity> New(Id id)
        {
            return New(Defaults.Bucket, id);
        }

        public async Task<TEntity> New(string bucket, Id id)
        {
            TEntity root;
            var cacheId = $"{bucket}.{id}";
            if (!Tracked.TryGetValue(cacheId, out root))
            {
                root = await NewUntracked(bucket, id).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }
            return root;
        }
        protected virtual Task<TEntity> NewUntracked(string bucket, Id id, Id[] parents = null)
        {
            var entity = Factory.Create(bucket, id, parents);

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedStore).OobWriter = _oobStore;

            return Task.FromResult(entity);
        }

        public virtual Task<TEntity> TryGet(Id id)
        {
            return TryGet(Defaults.Bucket, id);
        }
        public async Task<TEntity> TryGet(string bucket, Id id)
        {
            if (id == null) return default(TEntity);

            try
            {
                return await Get(bucket, id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return default(TEntity);
        }

        public virtual IEventPlanner Plan(Id id)
        {
            return Plan(Defaults.Bucket, id);
        }
        public IEventPlanner Plan(string bucket, Id id)
        {
            return new EventPlanner<TState>(_eventstore, _snapstore, _factory, bucket, id);
        }
        public IChecker Check(Id id)
        {
            return Check(Defaults.Bucket, id);
        }
        public IChecker Check(string bucket, Id id)
        {
            var cacheId = $"{bucket}.{id}";
            if (!Tracked.ContainsKey(cacheId))
                throw new ExistException(typeof(TEntity), bucket, id);
            return new Checker<TEntity, TState>(_factory, Tracked[cacheId]);
        }

    }
}