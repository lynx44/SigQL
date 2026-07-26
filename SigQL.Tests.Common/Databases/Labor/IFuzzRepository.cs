using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SigQL.Types;
using SigQL.Types.Attributes;

namespace SigQL.Tests.Common.Databases.Labor
{
    /// <summary>
    /// Scratch surface for exploratory fuzzing of method signature combinations.
    /// </summary>
    public interface IFuzzRepository
    {
        // ---- nullable value type filters combined with comparison attributes ----
        IEnumerable<WorkLog.IWorkLogId> F_NullableGreaterThan([GreaterThan] DateTime? startDate);
        IEnumerable<WorkLog.IWorkLogId> F_NullableLessThanIgnoreIfNull([LessThan, IgnoreIfNull] DateTime? startDate);
        IEnumerable<WorkLog.IWorkLogId> F_NotGreaterThan([Not, GreaterThan] DateTime startDate);
        IEnumerable<WorkLog.IWorkLogId> F_BetweenNullableAliased(
            [Column(nameof(WorkLog.StartDate)), GreaterThanOrEqual, IgnoreIfNull] DateTime? from,
            [Column(nameof(WorkLog.StartDate)), LessThanOrEqual, IgnoreIfNull] DateTime? to);

        // ---- Not + null/collection handling ----
        IEnumerable<Employee.IEmployeeId> F_NotIgnoreIfNull([Not, IgnoreIfNull] string name);
        IEnumerable<Employee.IEmployeeId> F_NotInEmptyCollection([Not] IEnumerable<string> name);
        IEnumerable<Employee.IEmployeeId> F_NotContainsIgnoreIfNullOrEmpty([Not, Contains, IgnoreIfNullOrEmpty] string name);
        IEnumerable<Employee.IEmployeeId> F_NullableIdCollection(IEnumerable<int?> id);

        // ---- enum / guid filters ----
        IEnumerable<Address.IAddressFields> F_EnumFilter(AddressClassification classification);
        IEnumerable<Address.IAddressFields> F_NullableEnumFilter(AddressClassification? classification);
        IEnumerable<Address.IAddressFields> F_EnumCollectionFilter(IEnumerable<AddressClassification> classification);
        IEnumerable<Address.IAddressFields> F_EnumNotFilter([Not] AddressClassification classification);
        IEnumerable<CategoryItem.ICategoryItemFields> F_GuidFilter(Guid categoryId);
        IEnumerable<CategoryItem.ICategoryItemFields> F_NullableGuidFilter(Guid? categoryId);
        IEnumerable<CategoryItem.ICategoryItemFields> F_GuidCollectionFilter(IEnumerable<Guid> categoryId);
        IEnumerable<CategoryItem.ICategoryItemFields> F_GuidIgnoreIfNull([IgnoreIfNull] Guid? categoryId);

        // ---- offset/fetch variations ----
        IEnumerable<WorkLog.IWorkLogId> F_NullableOffsetFetch([Offset] int? skip, [Fetch] int? take);
        IEnumerable<WorkLog.IWorkLogId> F_FetchOnlyWithOrderBy([Fetch] int take, IOrderBy order);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> F_OffsetFetchWithIgnoredFilter([Offset] int skip, [Fetch] int take, [IgnoreIfNull, ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] string employeeName);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> F_OffsetFetchWithNullOrderBy([Offset] int skip, [Fetch] int take, IEnumerable<IOrderBy> order);
        IEnumerable<Employee.IEmployeeWithAddresses> F_OffsetFetchManyToMany([Offset] int skip, [Fetch] int take);

        // ---- count / total count ----
        ICountResult<Employee.IEmployeeId> F_CountWithFilter([IgnoreIfNull] string name);
        ICountResult<WorkLog.IWorkLogWithEmployeeNames> F_CountWithNavigationCollection();
        ITotalCount<Employee.IEmployeeId> F_TotalCountWithIgnoredFilter([IgnoreIfNull] string name, [Offset] int skip, [Fetch] int take);
        ITotalCountResult<IEnumerable<Employee.IEmployeeWithAddresses>> F_TotalCountResultManyToMany([Offset] int skip, [Fetch] int take);
        ITotalCountResult<IEnumerable<WorkLog.IWorkLogId>> F_TotalCountResultWithInFilter(IEnumerable<int> id, [Offset] int skip, [Fetch] int take);
        Task<ICountResult<Employee.IEmployeeId>> F_CountAsync();
        Task<ITotalCountResult<IEnumerable<Employee.IEmployeeId>>> F_TotalCountResultAsync([Offset] int skip, [Fetch] int take);

        // ---- scalar select variations ----
        [Select(TableName = nameof(Category), ColumnName = nameof(Category.Id))]
        IEnumerable<Guid> F_ScalarGuids();
        [Select(TableName = nameof(CategoryItem), ColumnName = nameof(CategoryItem.CategoryId))]
        IEnumerable<Guid?> F_ScalarNullableGuids();
        [Select(TableName = nameof(Address), ColumnName = nameof(Address.Classification))]
        IEnumerable<AddressClassification?> F_ScalarNullableEnums();
        [Select(TableName = nameof(WorkLog), ColumnName = nameof(WorkLog.Id))]
        IEnumerable<int> F_ScalarWithFetchAndOrder([Fetch] int take, IOrderBy order);
        [Select(TableName = nameof(Employee), ColumnName = nameof(Employee.Name))]
        Task<IEnumerable<string>> F_ScalarEnumerableAsync();
        [Select(TableName = nameof(Employee), ColumnName = nameof(Employee.Name))]
        List<string> F_ScalarList();
        [Select(TableName = nameof(WorkLog), ColumnName = nameof(WorkLog.Id))]
        IEnumerable<int> F_ScalarViaRelationCollection([ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] IEnumerable<string> names);

        // ---- async variants of common shapes ----
        Task<Employee.IEmployeeFields> F_SingleAsync(int id);
        Task<IEnumerable<Employee.IEmployeeWithAddresses>> F_ManyToManyAsync();
        Task<List<Employee.IEmployeeId>> F_ListAsync();

        // ---- projection collection type variations ----
        List<Employee.IEmployeeId> F_ReturnList();
        IReadOnlyList<Employee.IEmployeeId> F_ReturnIReadOnlyList();
        Employee.IEmployeeId[] F_ReturnArray();
        ICollection<Employee.IEmployeeId> F_ReturnICollection();

        // ---- or groups combined with other attributes ----
        IEnumerable<Employee.IEmployeeId> F_OrGroupWithIgnoreIfNull([OrGroup, IgnoreIfNull] string name, [OrGroup, IgnoreIfNull] int? id);
        IEnumerable<Employee.IEmployeeId> F_OrGroupSingleMember([OrGroup] string name);
        IEnumerable<Employee.IEmployeeId> F_OrGroupWithContains([OrGroup, Contains] string name, [OrGroup] int id);
        IEnumerable<WorkLog.IWorkLogId> F_OrGroupWithCollection([OrGroup] IEnumerable<int> id, [OrGroup] IEnumerable<int> employeeId);
        IEnumerable<WorkLog.IWorkLogId> F_OrGroupWithOffsetFetch([OrGroup] int id, [OrGroup] int employeeId, [Offset] int skip, [Fetch] int take);

        // ---- via relation combos ----
        IEnumerable<WorkLog.IWorkLogId> F_ViaRelationNotIn([Not, ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] IEnumerable<string> names);
        IEnumerable<WorkLog.IWorkLogId> F_ViaRelationIgnoreIfNull([IgnoreIfNull, ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] string name);
        IEnumerable<WorkLog.IWorkLogId> F_ViaRelationGreaterThan([GreaterThan, ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Id))] int employeeId);
        IEnumerable<WorkLog.IWorkLogId> F_ViaRelationStartsWith([StartsWith, ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] string name);

        // ---- like variations ----
        IEnumerable<Employee.IEmployeeId> F_LikeIgnoreIfNull([IgnoreIfNull] Like name);
        IEnumerable<Employee.IEmployeeId> F_LikeCollection([Column(nameof(Employee.Name))] IEnumerable<Like> names);
        IEnumerable<Employee.IEmployeeId> F_StartsWithNullable([StartsWith, IgnoreIfNull] string name);

        // ---- update / delete combos ----
        [Update(TableName = nameof(Employee))]
        void F_UpdateWithInFilter([Set] string name, IEnumerable<int> id);
        [Update(TableName = nameof(Employee))]
        void F_UpdateWithNotFilter([Set] string name, [Not] int id);
        [Update(TableName = nameof(WorkLog))]
        void F_UpdateWithGreaterThanFilter([Set] DateTime startDate, [GreaterThan, Column(nameof(WorkLog.EndDate))] DateTime endDateAfter);
        [Delete(TableName = nameof(WorkLog))]
        void F_DeleteWithIgnoreIfNull([IgnoreIfNull] int? id);
        [Delete(TableName = nameof(WorkLog))]
        void F_DeleteWithGreaterThan([GreaterThan] DateTime startDate);
        [Delete(TableName = nameof(WorkLog))]
        void F_DeleteAll();
        [Delete(TableName = nameof(WorkLog))]
        void F_DeleteViaRelation([ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] string name);

        // ---- insert / upsert combos ----
        [Insert(TableName = nameof(Employee))]
        IEnumerable<Employee.IEmployeeId> F_InsertEmptyCollectionReturnIds(IEnumerable<Employee.InsertFields> employees);
        [Insert(TableName = nameof(Category))]
        void F_InsertGuidKeyed(IEnumerable<Category.InsertFields> categories);
        [Upsert(TableName = nameof(Employee))]
        IEnumerable<Employee.IEmployeeId> F_UpsertSingleReturnIds(Employee.UpsertFieldsByName employee);
        [Insert(TableName = nameof(Employee))]
        Task F_InsertAsync(IEnumerable<Employee.InsertFields> employees);

        // ================= batch 2 =================

        // ---- filters that end up fully ignored, combined with paging/count ----
        IEnumerable<WorkLog.IWorkLogId> F_AllFiltersIgnored([IgnoreIfNull] int? id, [IgnoreIfNull] int? employeeId);
        IEnumerable<WorkLog.IWorkLogId> F_AllFiltersIgnoredViaClassFilter(FuzzFilters.AllIgnorable filter);
        ITotalCountResult<IEnumerable<WorkLog.IWorkLogId>> F_IgnoredFilterWithTotalCount(FuzzFilters.AllIgnorable filter, [Offset] int skip, [Fetch] int take);
        ICountResult<WorkLog.IWorkLogId> F_IgnoredFilterWithCount(FuzzFilters.AllIgnorable filter);
        IEnumerable<WorkLog.IWorkLogId> F_IgnoredNavigationFilter(FuzzFilters.IgnorableNavigation filter);
        ITotalCountResult<IEnumerable<WorkLog.IWorkLogId>> F_IgnoredNavigationFilterWithTotalCount(FuzzFilters.IgnorableNavigation filter, [Offset] int skip, [Fetch] int take);
        IEnumerable<WorkLog.IWorkLogId> F_IgnoredOrGroup(FuzzFilters.IgnorableOrGroup filter);
        ITotalCountResult<IEnumerable<WorkLog.IWorkLogId>> F_IgnoredOrGroupWithTotalCount(FuzzFilters.IgnorableOrGroup filter, [Offset] int skip, [Fetch] int take);

        // ---- nested class filters ----
        IEnumerable<WorkLog.IWorkLogId> F_TwoLevelNestedFilter(FuzzFilters.WorkLogEmployeeAddress filter);
        IEnumerable<WorkLog.IWorkLogId> F_NestedFilterWithCollection(FuzzFilters.EmployeeNamesNested filter);
        IEnumerable<WorkLog.IWorkLogId> F_NestedFilterWithEnum(FuzzFilters.LocationAddressClassification filter);
        IEnumerable<Employee.IEmployeeId> F_ManyToManyNestedCollectionFilter(FuzzFilters.EmployeeAddressCities filter);

        // ---- enum via relation ----
        IEnumerable<Employee.IEmployeeId> F_EnumViaRelation([ViaRelation(nameof(Employee) + "->EFAddressEFEmployee->" + nameof(Address), nameof(Address.Classification))] AddressClassification classification);
        IEnumerable<Employee.IEmployeeId> F_EnumCollectionViaRelation([ViaRelation(nameof(Employee) + "->EFAddressEFEmployee->" + nameof(Address), nameof(Address.Classification))] IEnumerable<AddressClassification> classification);

        // ---- projections ----
        IEnumerable<FuzzProjections.WorkLogWithEmployeeAndLocationEmployees> F_SameTableTwiceInProjection();
        IEnumerable<FuzzProjections.WorkLogWithEmployeeAndLocationEmployees> F_SameTableTwiceInProjectionPaged([Offset] int skip, [Fetch] int take);
        IEnumerable<FuzzProjections.EmployeeWithClrOnlyAndAddresses> F_ClrOnlyWithNavigationCollection();
        IEnumerable<FuzzProjections.EmployeeWithAddressesPocoNested> F_PocoNestedCollectionPaged([Offset] int skip, [Fetch] int take);
        FuzzProjections.EmployeeWithAddresses F_SinglePocoWithCollection(int id);
        IEnumerable<FuzzProjections.WorkLogDeepNesting> F_DeepNesting();

        // ---- update ----
        [Update(TableName = nameof(Employee))]
        void F_UpdateAllSetsIgnorable([Set, IgnoreIfNull] string name, int id);
        [Update(TableName = nameof(WorkLog))]
        void F_UpdateSetClassAllIgnorable(FuzzFilters.SetDatesIgnorable workLog);
        [Update(TableName = nameof(Employee))]
        void F_UpdateNoFilter([Set] string name);

        // ---- delete ----
        [Delete(TableName = nameof(WorkLog))]
        void F_DeleteByInCollection([Column(nameof(WorkLog.Id))] IEnumerable<int> id);
        [Delete(TableName = nameof(WorkLog))]
        void F_DeleteByOrGroup([OrGroup] int id, [OrGroup] int employeeId);
        [Delete(TableName = nameof(WorkLog))]
        void F_DeleteByClassFilter(WorkLog.GetByEmployeeNameFilter filter);

        // ================= batch 3 =================

        // ---- views and keyless tables with paging / counting ----
        IEnumerable<WorkLogEmployeeView.IFields> F_ViewWithOffsetFetch([Offset] int skip, [Fetch] int take);
        IEnumerable<WorkLogEmployeeView.IFields> F_ViewWithFetch([Fetch] int take);
        IEnumerable<WorkLogEmployeeView.IFields> F_ViewWithFilter(int employeeId);
        ICountResult<WorkLogEmployeeView.IFields> F_ViewCount();
        ITotalCountResult<IEnumerable<WorkLogEmployeeView.IFields>> F_ViewTotalCountResult([Offset] int skip, [Fetch] int take);
        IEnumerable<WorkLogEmployeeView.IDataFieldsWithWorkLogs> F_ViewWithJoinRelationPaged([Offset] int skip, [Fetch] int take);
        ICountResult<DiagnosticLog.IFields> F_KeylessTableCount();
        IEnumerable<DiagnosticLog.IFields> F_KeylessTableOffsetFetch([Offset] int skip, [Fetch] int take);

        // ---- functions ----
        IEnumerable<itvf_GetWorkLogsByEmployeeId.IId> F_FunctionWithFetch([Parameter] int empId, [Fetch] int take);
        ICountResult<itvf_GetWorkLogsByEmployeeId.IId> F_FunctionCount([Parameter] int empId);
        IEnumerable<itvf_GetWorkLogsByEmployeeId.IId> F_FunctionWithInFilter([Parameter] int empId, IEnumerable<int> id);

        // ---- composite primary keys ----
        [Insert]
        void F_InsertCompositeKey(IEnumerable<CompositeKeyTable.IFields> values);
        [Upsert(TableName = nameof(CompositeKeyTable))]
        void F_UpsertCompositeKey(IEnumerable<CompositeKeyTable.Fields> values);
        [UpdateByKey(TableName = nameof(CompositeKeyTable))]
        void F_UpdateByKeyCompositeKey(IEnumerable<CompositeKeyTable.Fields> values);
        [Delete(TableName = nameof(CompositeKeyTable))]
        void F_DeleteCompositeKey(string firstName, string lastName);
        ICountResult<CompositeKeyTable.IFields> F_CompositeKeyCount();
        ITotalCountResult<IEnumerable<CompositeKeyTable.IFieldsWithChildren>> F_CompositeKeyTotalCountResult([Offset] int skip, [Fetch] int take);
        IEnumerable<CompositeForeignKeyTable.IFieldsWithParent> F_CompositeForeignKeyPaged([Offset] int skip, [Fetch] int take);

        // ---- order by variations ----
        IEnumerable<WorkLog.IWorkLogId> F_OrderByDescendingDynamic(IOrderBy order);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> F_OrderByStaticAndDynamic(
            [Column(nameof(WorkLog.StartDate))] OrderByDirection startDate, IEnumerable<IOrderBy> order);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> F_OrderByRelationWithOffset([Offset] int skip, IEnumerable<OrderByRelation> order);
        IEnumerable<Employee.IEmployeeWithAddresses> F_OrderByOneToManyWithOffsetFetch([Offset] int skip, [Fetch] int take, IEnumerable<IOrderBy> order);

        // ---- same column referenced by two parameters ----
        IEnumerable<WorkLog.IWorkLogId> F_SameColumnTwoParameters(
            [Column(nameof(WorkLog.Id))] IEnumerable<int> includeIds,
            [Column(nameof(WorkLog.Id)), Not] IEnumerable<int> excludeIds);

        // ================= batch 4 =================

        // ---- async write methods ----
        [Update(TableName = nameof(Employee))]
        Task F_UpdateAsync([Set] string name, int id);
        [Delete(TableName = nameof(WorkLog))]
        Task F_DeleteAsync(int id);
        [Upsert(TableName = nameof(Employee))]
        Task F_UpsertAsync(IEnumerable<Employee.UpsertFieldsByName> employees);
        [Sync]
        Task F_SyncAsync(Employee.SyncFieldsWithWorkLogs employees);
        [Insert]
        Task<IEnumerable<Employee.IEmployeeId>> F_InsertAsyncReturnIds(IEnumerable<Employee.InsertFields> employees);

        // ---- guid keyed writes ----
        [Upsert(TableName = nameof(Category))]
        void F_UpsertGuidKeyed(IEnumerable<Category.InsertFields> categories);
        [Sync(TableName = nameof(Category))]
        void F_SyncGuidKeyed(Category.InsertFields category);
        [Delete(TableName = nameof(Category))]
        void F_DeleteGuidKeyed(Guid id);

        // ---- mixed attribute stacks on one method ----
        IEnumerable<WorkLog.IWorkLogId> F_KitchenSink(
            [OrGroup("a"), IgnoreIfNullOrEmpty] IEnumerable<int> id,
            [OrGroup("a"), IgnoreIfNull, ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] string employeeName,
            [OrGroup("b"), GreaterThanOrEqual, IgnoreIfNull, Column(nameof(WorkLog.StartDate))] DateTime? from,
            [OrGroup("b"), LessThanOrEqual, IgnoreIfNull, Column(nameof(WorkLog.StartDate))] DateTime? to,
            [ClrOnly] string ignored,
            [Offset] int? skip,
            [Fetch] int? take,
            IEnumerable<IOrderBy> order);
        ITotalCountResult<IEnumerable<WorkLog.IWorkLogWithEmployeeNames>> F_KitchenSinkWithTotalCount(
            [OrGroup("a"), IgnoreIfNullOrEmpty] IEnumerable<int> id,
            [OrGroup("a"), IgnoreIfNull, ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] string employeeName,
            [Offset] int skip,
            [Fetch] int take,
            IEnumerable<IOrderBy> order);

        // ---- single result with a default fetch ----
        Employee.IEmployeeFields F_SingleWithDefaultFetch(string name, [Fetch] int fetch = 1);
        Employee.IEmployeeFields F_SingleWithNoFetch(int id);

        // ---- enum columns in projections ----
        IEnumerable<FuzzProjections.AddressAliasedEnum> F_AliasedEnumProjection();
        [Select(TableName = nameof(Address), ColumnName = nameof(Address.Classification))]
        AddressClassification? F_ScalarSingleNullableEnum(int id);
    }

    public class FuzzFilters
    {
        [SqlIdentifier(nameof(WorkLog))]
        public class AllIgnorable
        {
            [IgnoreIfNull] public int? Id { get; set; }
            [IgnoreIfNull] public int? EmployeeId { get; set; }
        }

        [SqlIdentifier(nameof(WorkLog))]
        public class IgnorableNavigation
        {
            [IgnoreIfNull] public int? Id { get; set; }
            public Employee.EmployeeNameIgnorable Employee { get; set; }
        }

        [SqlIdentifier(nameof(WorkLog))]
        public class IgnorableOrGroup
        {
            [OrGroup, IgnoreIfNull] public int? Id { get; set; }
            [OrGroup, IgnoreIfNull] public int? EmployeeId { get; set; }
        }

        [SqlIdentifier(nameof(WorkLog))]
        public class WorkLogEmployeeAddress
        {
            public EmployeeAddressNested Employee { get; set; }
        }

        [SqlIdentifier(nameof(Employee))]
        public class EmployeeAddressNested
        {
            public string Name { get; set; }
            public Address.StreetAddressFilter Addresses { get; set; }
        }

        [SqlIdentifier(nameof(WorkLog))]
        public class EmployeeNamesNested
        {
            public EmployeeNames Employee { get; set; }
        }

        [SqlIdentifier(nameof(Employee))]
        public class EmployeeNames
        {
            public IEnumerable<string> Name { get; set; }
        }

        [SqlIdentifier(nameof(WorkLog))]
        public class LocationAddressClassification
        {
            public LocationAddress Location { get; set; }
        }

        [SqlIdentifier(nameof(Location))]
        public class LocationAddress
        {
            public AddressClassificationFilter Address { get; set; }
        }

        [SqlIdentifier(nameof(Address))]
        public class AddressClassificationFilter
        {
            public AddressClassification Classification { get; set; }
        }

        [SqlIdentifier(nameof(Employee))]
        public class EmployeeAddressCities
        {
            public AddressCities Addresses { get; set; }
        }

        [SqlIdentifier(nameof(Address))]
        public class AddressCities
        {
            public IEnumerable<string> City { get; set; }
        }

        [SqlIdentifier(nameof(WorkLog))]
        public class SetDatesIgnorable
        {
            [Set, IgnoreIfNull] public DateTime? StartDate { get; set; }
            [Set, IgnoreIfNull] public DateTime? EndDate { get; set; }
            public int Id { get; set; }
        }
    }

    public class FuzzProjections
    {
        [SqlIdentifier(nameof(WorkLog))]
        public interface WorkLogWithEmployeeAndLocationEmployees
        {
            int Id { get; }
            Employee.IEmployeeFields Employee { get; }
            LocationWithAddressEmployees Location { get; }
        }

        [SqlIdentifier(nameof(Location))]
        public interface LocationWithAddressEmployees
        {
            int Id { get; }
            AddressWithEmployees Address { get; }
        }

        [SqlIdentifier(nameof(Address))]
        public interface AddressWithEmployees
        {
            int Id { get; }
            IEnumerable<Employee.IEmployeeFields> Employees { get; }
        }

        [SqlIdentifier(nameof(Employee))]
        public interface EmployeeWithClrOnlyAndAddresses
        {
            int Id { get; }
            IEnumerable<Address.IAddressFields> Addresses { get; }
            [ClrOnly] string Label => $"employee-{Id}";
        }

        [SqlIdentifier(nameof(Employee))]
        public class EmployeeWithAddressesPocoNested
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public List<AddressWithLocationsPoco> Addresses { get; set; }
        }

        [SqlIdentifier(nameof(Address))]
        public class AddressWithLocationsPoco
        {
            public int Id { get; set; }
            public string City { get; set; }
            public List<LocationPoco> Locations { get; set; }
        }

        [SqlIdentifier(nameof(Location))]
        public class LocationPoco
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        [SqlIdentifier(nameof(Employee))]
        public class EmployeeWithAddresses
        {
            public int Id { get; set; }
            public IEnumerable<Address.IAddressFields> Addresses { get; set; }
        }

        [SqlIdentifier(nameof(WorkLog))]
        public interface WorkLogDeepNesting
        {
            int Id { get; }
            EmployeeDeep Employee { get; }
        }

        [SqlIdentifier(nameof(Employee))]
        public interface EmployeeDeep
        {
            int Id { get; }
            IEnumerable<AddressDeep> Addresses { get; }
        }

        [SqlIdentifier(nameof(Address))]
        public interface AddressDeep
        {
            int Id { get; }
            IEnumerable<LocationDeep> Locations { get; }
        }

        [SqlIdentifier(nameof(Location))]
        public interface LocationDeep
        {
            int Id { get; }
            string Name { get; }
        }

        [SqlIdentifier(nameof(Address))]
        public interface AddressAliasedEnum
        {
            int Id { get; }
            [Column(nameof(Address.Classification))] AddressClassification Kind { get; }
            [Column(nameof(Address.Classification))] AddressClassification? NullableKind { get; }
        }
    }
}
