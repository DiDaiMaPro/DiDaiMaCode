using Autofac.Extras.DynamicProxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExpressCode.Services.Admin.User
{
    [Intercept(typeof(AopTest))]
    public interface IUserService
    {
        UserGetOutput GetUser();
    }
}
