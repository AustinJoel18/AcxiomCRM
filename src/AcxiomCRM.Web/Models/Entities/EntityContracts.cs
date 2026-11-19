namespace AcxiomCRM.Web.Models.Entities;

/// <summary>Records that belong to (are assigned to) a single user. Drives data-scope authorization.</summary>
public interface IOwnedEntity
{
    string AssignedToId { get; set; }
}

/// <summary>Records whose creation/modification metadata is stamped automatically by the DbContext.</summary>
public interface ITrackedEntity
{
    DateTime CreatedDate { get; set; }
    string? CreatedById { get; set; }
    DateTime? ModifiedDate { get; set; }
    string? ModifiedById { get; set; }
}

/// <summary>Records that are deactivated instead of physically deleted.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedDate { get; set; }
    string? DeletedById { get; set; }
}

/// <summary>Marker: create/update/delete of these records is written to the audit log automatically.</summary>
public interface IAuditableEntity;
