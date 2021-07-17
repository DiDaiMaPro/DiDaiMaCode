using Autofac.Extras.DynamicProxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExpressCode.Model;

namespace ExpressCode.Repository
{
    [Intercept(typeof(AopTest))]
    public interface IUserRepository
    {
        UserEntity test();
    }
}
