using AcxiomCRM.Web.Domain;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Models.Entities;
using AcxiomCRM.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcxiomCRM.Web.Data;

/// <summary>
/// Applies migrations, ensures the three roles exist, bootstraps the first Admin and (optionally,
/// development only) loads realistic demo data. Credentials come from configuration, never code.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, bool migrate = true)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
        var seed = sp.GetRequiredService<IOptions<SeedOptions>>().Value;

        if (migrate) await db.Database.MigrateAsync();

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AppRoles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var clock = sp.GetRequiredService<IBusinessClock>();

        if ((await users.GetUsersInRoleAsync(AppRoles.Admin)).Count == 0)
        {
            var password = seed.AdminPassword ?? seed.DemoPassword;
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("No Admin exists and Seed:AdminPassword is not configured; skipping admin bootstrap.");
            }
            else
            {
                await CreateUserAsync(users, clock, seed.AdminEmail, "Priya Sharma", AppRoles.Admin, password, null);
                logger.LogInformation("Bootstrapped Admin account {Email}.", seed.AdminEmail);
            }
        }

        if (seed.DemoData && !string.IsNullOrWhiteSpace(seed.DemoPassword) && !await db.Customers.IgnoreQueryFilters().AnyAsync())
        {
            await SeedDemoAsync(db, users, clock, seed.DemoPassword!);
            logger.LogInformation("Demo data loaded.");
        }
    }

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> users, IBusinessClock clock, string email,
        string name, string role, string password, string? managerId, string? phone = null)
    {
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null) return existing;
        var user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = true, FullName = name, IsActive = true, PhoneNumber = phone,
            ManagerId = managerId, CreatedDate = clock.UtcNow.AddDays(-400), LockoutEnabled = true
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not seed {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        await users.AddToRoleAsync(user, role);
        return user;
    }

    private static async Task SeedDemoAsync(ApplicationDbContext db, UserManager<ApplicationUser> users, IBusinessClock clock, string password)
    {
        var rnd = new Random(20261119);
        var r1 = await CreateUserAsync(users, clock, "manager@acxiomcrm.local", "Rahul Verma", AppRoles.Manager, password, null, "9810011001");
        var r2 = await CreateUserAsync(users, clock, "manager2@acxiomcrm.local", "Ananya Iyer", AppRoles.Manager, password, null, "9810011002");
        var s1 = await CreateUserAsync(users, clock, "sales@acxiomcrm.local", "Vikram Singh", AppRoles.SalesExecutive, password, r1.Id, "9810011003");
        var s2 = await CreateUserAsync(users, clock, "sales2@acxiomcrm.local", "Sneha Patel", AppRoles.SalesExecutive, password, r1.Id, "9810011004");
        var s3 = await CreateUserAsync(users, clock, "sales3@acxiomcrm.local", "Arjun Nair", AppRoles.SalesExecutive, password, r2.Id, "9810011005");
        var s4 = await CreateUserAsync(users, clock, "sales4@acxiomcrm.local", "Kavya Reddy", AppRoles.SalesExecutive, password, r2.Id, "9810011006");
        string[] owners = [s1.Id, s2.Id, s3.Id, s4.Id, s1.Id, s3.Id, r1.Id, r2.Id];

        db.AutoAuditEnabled = false;
        var nowUtc = clock.UtcNow;
        var today = clock.Today;

        (string City, string State)[] places =
        [
            ("Mumbai", "Maharashtra"), ("Pune", "Maharashtra"), ("Bengaluru", "Karnataka"), ("Chennai", "Tamil Nadu"),
            ("Hyderabad", "Telangana"), ("New Delhi", "Delhi"), ("Ahmedabad", "Gujarat"), ("Kolkata", "West Bengal"),
            ("Jaipur", "Rajasthan"), ("Kochi", "Kerala")
        ];
        string[] companies =
        [
            "Tata Lumen Systems", "Infinova Software", "Bharat Agro Exports", "Sahyadri Logistics", "Coastal Pharma Labs",
            "Northstar Textiles", "Ganga Foods", "Vertex Fintech", "Lotus Hospitality", "Deccan Steelworks", "Kaveri Healthcare",
            "Indus Retail Mart", "Skyline Builders", "Monsoon Media", "Saffron Motors", "Peacock Electronics", "Narmada Chemicals",
            "Himalaya Outdoor", "Sunrise Solar", "Konark Packaging", "Malabar Spices", "Orbit Telecom", "Zenith EduTech",
            "Silverline Insurance", "Golden Harvest Dairy", "Nilgiri Tea Estates", "Arcadia Interiors", "Pinnacle Consulting"
        ];
        string[] firstNames = ["Amit", "Neha", "Rohan", "Pooja", "Suresh", "Divya", "Karan", "Meera", "Sanjay", "Ritu", "Nikhil", "Isha", "Manish", "Lakshmi", "Varun", "Shreya"];
        string[] lastNames = ["Kapoor", "Mehta", "Joshi", "Rao", "Menon", "Gupta", "Bose", "Desai", "Kulkarni", "Chopra", "Pillai", "Saxena"];
        string Person(int i) => $"{firstNames[i % firstNames.Length]} {lastNames[(i * 7 + 3) % lastNames.Length]}";
        string Slug(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        // Customers
        var customers = new List<Customer>();
        for (var i = 0; i < companies.Length; i++)
        {
            var place = places[i % places.Length];
            customers.Add(new Customer
            {
                CustomerCode = $"CUS-{i + 1:D5}",
                CustomerName = Person(i),
                CompanyName = companies[i],
                Email = $"{Slug(Person(i))}@{Slug(companies[i])}.in",
                Phone = $"9{820000000 + i * 1373}",
                Address = $"{10 + i}, {(i % 2 == 0 ? "MG Road" : "Station Road")}",
                City = place.City,
                State = place.State,
                Status = i % 9 == 0 ? CustomerStatus.Inactive : i % 5 == 0 ? CustomerStatus.Prospect : CustomerStatus.Active,
                Notes = i % 3 == 0 ? "Key account. Prefers email communication." : null,
                AssignedToId = owners[i % owners.Length],
                CreatedDate = nowUtc.AddDays(-rnd.Next(20, 380)),
                CreatedById = owners[i % owners.Length]
            });
        }
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();

        // Leads
        LeadStatus[] leadStatuses =
        [
            LeadStatus.New, LeadStatus.New, LeadStatus.Contacted, LeadStatus.Contacted, LeadStatus.Qualified, LeadStatus.Qualified,
            LeadStatus.Unqualified, LeadStatus.Lost, LeadStatus.Converted, LeadStatus.Converted
        ];
        var leads = new List<Lead>();
        for (var i = 0; i < 40; i++)
        {
            var status = leadStatuses[i % leadStatuses.Length];
            var owner = owners[(i + 1) % owners.Length];
            var created = nowUtc.AddDays(-rnd.Next(1, 300));
            var lead = new Lead
            {
                LeadCode = $"LEA-{i + 1:D5}",
                LeadName = Person(i + 5),
                CompanyName = i % 4 == 0 ? null : $"{lastNames[i % lastNames.Length]} {(i % 3 == 0 ? "Enterprises" : i % 3 == 1 ? "Industries" : "Technologies")}",
                Email = $"lead{i + 1}@example.in",
                Phone = i % 5 == 0 ? null : $"9{730000000 + i * 911}",
                Source = (LeadSource)(i % Enum.GetValues<LeadSource>().Length),
                Status = status,
                Priority = (LeadPriority)(i % 3),
                ExpectedValue = 50000 + rnd.Next(0, 40) * 25000,
                Notes = status == LeadStatus.Lost ? "Chose a competitor on price." : null,
                AssignedToId = owner,
                CreatedById = owner,
                CreatedDate = created
            };
            if (status == LeadStatus.Converted)
            {
                var customer = customers[i % customers.Count];
                lead.ConvertedCustomerId = customer.CustomerId;
                lead.ConvertedDate = created.AddDays(rnd.Next(3, 20));
                lead.AssignedToId = customer.AssignedToId;
            }
            leads.Add(lead);
        }
        db.Leads.AddRange(leads);
        await db.SaveChangesAsync();

        // Opportunities: open pipeline (future close dates) + 12 months of won/lost history.
        var opps = new List<Opportunity>();
        string[] products = ["CRM Rollout", "Annual Support Contract", "Cloud Migration", "Analytics Suite", "Fleet Tracking", "ERP Integration", "Security Audit", "Data Platform"];
        for (var i = 0; i < 44; i++)
        {
            var customer = customers[(i * 5) % customers.Count];
            OpportunityStage stage = i < 20 ? OpportunityRules.ActiveStages[i % 3] : i % 3 == 0 ? OpportunityStage.Lost : OpportunityStage.Won;
            var amount = 75000m + rnd.Next(1, 60) * 15000m;
            var o = new Opportunity
            {
                OpportunityName = $"{customer.CompanyName} — {products[i % products.Length]}",
                CustomerId = customer.CustomerId,
                LeadId = i % 6 == 0 ? leads.FirstOrDefault(l => l.ConvertedCustomerId == customer.CustomerId)?.LeadId : null,
                Amount = amount,
                Stage = stage,
                Status = OpportunityRules.StatusFor(stage),
                Probability = OpportunityRules.DefaultProbability(stage) + (OpportunityRules.IsActive(stage) ? rnd.Next(-5, 6) : 0),
                Source = (LeadSource)(i % 6),
                AssignedToId = customer.AssignedToId,
                CreatedById = customer.AssignedToId
            };
            if (OpportunityRules.IsActive(stage))
            {
                o.CreatedDate = nowUtc.AddDays(-rnd.Next(5, 120));
                o.ExpectedCloseDate = today.AddDays(rnd.Next(5, 120));
            }
            else
            {
                var closed = nowUtc.AddDays(-rnd.Next(1, 360));
                o.ClosedDate = closed;
                o.CreatedDate = closed.AddDays(-rnd.Next(20, 90));
                o.ExpectedCloseDate = clock.ToLocal(closed).Date;
                o.CloseReason = stage == OpportunityStage.Won ? "Contract signed." : "Budget not approved this year.";
            }
            opps.Add(o);
        }
        db.Opportunities.AddRange(opps);
        await db.SaveChangesAsync();
        foreach (var lead in leads.Where(l => l.ConvertedCustomerId is not null))
            lead.ConvertedOpportunityId = opps.FirstOrDefault(o => o.LeadId == lead.LeadId)?.OpportunityId;
        await db.SaveChangesAsync();

        // Follow-ups: overdue, today, upcoming, completed, missed, cancelled.
        string[] subjects = ["Discuss proposal", "Product demo", "Pricing negotiation", "Requirement walkthrough", "Contract review", "Renewal discussion", "Check-in call", "Site visit"];
        var followUps = new List<FollowUp>();
        var openLeads = leads.Where(l => l.Status is LeadStatus.New or LeadStatus.Contacted or LeadStatus.Qualified).ToList();
        var openOpps = opps.Where(o => o.Status == OpportunityStatus.Open).ToList();
        for (var i = 0; i < 48; i++)
        {
            var bucket = i % 6;
            var f = new FollowUp
            {
                Subject = subjects[i % subjects.Length],
                FollowUpType = (FollowUpType)(i % Enum.GetValues<FollowUpType>().Length),
                Remarks = i % 4 == 0 ? "Customer asked for a revised quote." : null
            };
            switch (i % 3)
            {
                case 0: { var c = customers[i % customers.Count]; f.CustomerId = c.CustomerId; f.AssignedToId = c.AssignedToId; break; }
                case 1: { var l = openLeads[i % openLeads.Count]; f.LeadId = l.LeadId; f.AssignedToId = l.AssignedToId; break; }
                default: { var o = openOpps[i % openOpps.Count]; f.OpportunityId = o.OpportunityId; f.AssignedToId = o.AssignedToId; break; }
            }
            var hour = 10 + i % 7;
            switch (bucket)
            {
                case 0: f.FollowUpDate = today.AddDays(-rnd.Next(1, 6)).AddHours(hour); f.Status = FollowUpStatus.Planned; break;
                case 1: f.FollowUpDate = today.AddHours(hour); f.Status = FollowUpStatus.Planned; break;
                case 2: f.FollowUpDate = today.AddDays(rnd.Next(1, 15)).AddHours(hour); f.Status = FollowUpStatus.Planned; break;
                case 3:
                    f.FollowUpDate = today.AddDays(-rnd.Next(3, 60)).AddHours(hour); f.Status = FollowUpStatus.Completed;
                    f.CompletedDate = clock.ToUtc(f.FollowUpDate.AddHours(1)); f.Outcome = "Spoke with the decision maker; next steps agreed."; break;
                case 4: f.FollowUpDate = today.AddDays(-rnd.Next(7, 40)).AddHours(hour); f.Status = FollowUpStatus.Missed; break;
                default: f.FollowUpDate = today.AddDays(rnd.Next(2, 20)).AddHours(hour); f.Status = FollowUpStatus.Cancelled; break;
            }
            f.CreatedDate = clock.ToUtc(f.FollowUpDate.AddDays(-rnd.Next(2, 10)));
            if (f.CreatedDate > nowUtc) f.CreatedDate = nowUtc.AddDays(-1);
            f.CreatedById = f.AssignedToId;
            followUps.Add(f);
        }
        db.FollowUps.AddRange(followUps);

        // Activities
        string[] activitySubjects = ["Intro call", "Quarterly business review", "Sent brochure", "Prepare quotation", "Onboarding meeting", "Follow-up email", "Demo preparation", "Internal pricing review"];
        for (var i = 0; i < 40; i++)
        {
            var type = (ActivityType)(i % 4);
            var past = i % 3 != 0;
            var a = new Activity
            {
                ActivityType = type,
                Subject = activitySubjects[i % activitySubjects.Length],
                Description = type == ActivityType.Task ? "Internal task." : "Discussed requirements and timelines.",
                ActivityDate = (past ? today.AddDays(-rnd.Next(1, 90)) : today.AddDays(rnd.Next(1, 20))).AddHours(9 + i % 8),
                Status = past ? ActivityStatus.Completed : i % 7 == 0 ? ActivityStatus.Cancelled : ActivityStatus.Planned
            };
            if (i % 2 == 0) { var c = customers[i % customers.Count]; a.CustomerId = c.CustomerId; a.AssignedToId = c.AssignedToId; }
            else { var l = leads[i % leads.Count]; a.LeadId = l.LeadId; a.AssignedToId = l.AssignedToId; }
            a.CreatedById = a.AssignedToId;
            a.CreatedDate = nowUtc.AddDays(-rnd.Next(1, 100));
            db.Activities.Add(a);
        }

        db.AuditLogs.Add(new AuditLog
        {
            CreatedDate = nowUtc, Module = AuditModule.System, Action = AuditAction.Seed, Result = AuditResult.Success,
            UserName = "system", Details = $"Demo data loaded: {customers.Count} customers, {leads.Count} leads, {opps.Count} opportunities, {followUps.Count} follow-ups, 40 activities."
        });
        await db.SaveChangesAsync();
        db.AutoAuditEnabled = true;
    }
}
