using System.Collections.Generic;

namespace SntBackend.Application.User.Dto
{
    public class UserSwitchBranchInput
    {
        /// <summary>
        /// Target company. Department is not required by the SNT organization model.
        /// </summary>
        public string company_pk { get; set; }

        /// <summary>
        /// Target branch. This is the required organization level for switching.
        /// </summary>
        public string branch_pk { get; set; }

        /// <summary>
        /// Kept as an optional compatibility field for older clients.
        /// </summary>
        public string dept_pk { get; set; }

        /// <summary>
        /// Legacy department-only payload. New clients should send branch_pk instead.
        /// </summary>
        public List<string> dept_pks { get; set; }
    }
}
