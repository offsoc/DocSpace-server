// (c) Copyright Ascensio System SIA 2009-2024
//
// This program is a free software product.
// You can redistribute it and/or modify it under the terms
// of the GNU Affero General Public License (AGPL) version 3 as published by the Free Software
// Foundation. In accordance with Section 7(a) of the GNU AGPL its Section 15 shall be amended
// to the effect that Ascensio System SIA expressly excludes the warranty of non-infringement of
// any third-party rights.
//
// This program is distributed WITHOUT ANY WARRANTY, without even the implied warranty
// of MERCHANTABILITY or FITNESS FOR A PARTICULAR  PURPOSE. For details, see
// the GNU AGPL at: http://www.gnu.org/licenses/agpl-3.0.html
//
// You can contact Ascensio System SIA at Lubanas st. 125a-25, Riga, Latvia, EU, LV-1021.
//
// The  interactive user interfaces in modified source and object code versions of the Program must
// display Appropriate Legal Notices, as required under Section 5 of the GNU AGPL version 3.
//
// Pursuant to Section 7(b) of the License you must retain the original Product logo when
// distributing the program. Pursuant to Section 7(e) we decline to grant you any rights under
// trademark law for use of our trademarks.
//
// All the Product's GUI elements, including illustrations and icon sets, as well as technical writing
// content are licensed under the terms of the Creative Commons Attribution-ShareAlike 4.0
// International. See the License terms at http://creativecommons.org/licenses/by-sa/4.0/legalcode

namespace ASC.MigrationFromPersonal.EF;

public class MigrationRecord
{
    public string Email { get; set; }
    public DateTime RequestDate { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public MigrationStatus Status { get; set; }
    public string Alias { get; set; }
    public TimeSpan MigtationTime { get; set; }
}

public static class MigrationRecordExtension
{
    public static ModelBuilderWrapper AddMigrationRecord(this ModelBuilderWrapper modelBuilder)
    {
        modelBuilder
            .Add(MySqlAddMigrationRecord, Provider.MySql);

        return modelBuilder;
    }

    public static void MySqlAddMigrationRecord(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MigrationRecord>(entity =>
        {
            entity.HasKey(e => new { e.Email })
                .HasName("PRIMARY");

            entity.ToTable("personal_to_docspace_request")
                .HasCharSet("utf8");

            entity.Property(e => e.RequestDate)
                .IsRequired()
                .HasColumnName("request_date")
                .HasColumnType("datetime");

            entity.Property(e => e.StartDate)
                .IsRequired()
                .HasColumnName("migration_start_date")
                .HasColumnType("datetime");

            entity.Property(e => e.EndDate)
                .IsRequired()
                .HasColumnName("migration_end_date")
                .HasColumnType("datetime");

            entity.Property(e => e.Alias)
                .HasColumnName("alias")
                .HasColumnType("varchar(100)")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Status)
                .HasColumnName("migration_status");

            entity.Property(e => e.Email)
                .HasColumnName("email")
                .HasColumnType("varchar(255)")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.MigtationTime)
               .HasColumnName("migtation_time")
               .HasColumnType("time");
        });
    }
}