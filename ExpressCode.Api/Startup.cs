using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExpressCode.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using NLog.Extensions.Logging;
using Autofac;

using Autofac.Extras.DynamicProxy;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Http;
using System.Reflection;
using ExpressCode.Common.Configs;

namespace ExpressCode.Api
{
    public class Startup
    {
        private readonly ConfigHelper _configHelper;
        private readonly IHostEnvironment _env;

        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            _env = env;
            _configHelper = new ConfigHelper();
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            #region 数据库链接

            var dbConfig = new ConfigHelper().Get<DbConfig>("dbconfig", _env.EnvironmentName);
            DBFactory.ConnectDB = dbConfig.UseDB;//在此切换数据库连接
            DBConfigHelper.ConnectSqlServer = dbConfig.SqlServerConStr;
            DBConfigHelper.ConnectMySql = dbConfig.MySqlConstr;

            #endregion

            #region 注册jwt
            JWTTokenOptions JWTTokenOptions = new JWTTokenOptions();
            //获取appsettings的内容
            JWTTokenOptions = new ConfigHelper().Get<JWTTokenOptions>("jwtconfig", _env.EnvironmentName);
            //将给定的对象实例绑定到指定的配置节点
            Configuration.Bind("JWTToken", JWTTokenOptions);

            //注册到Ioc容器
            services.AddSingleton(JWTTokenOptions);

            //【授权】
            services.AddAuthorization(options =>
            {
                options.AddPolicy("Client", policy => policy.RequireRole("Client").Build());
                options.AddPolicy("Admin", policy => policy.RequireRole("Admin").Build());
                options.AddPolicy("SystemOrAdmin", policy => policy.RequireRole("Admin", "System"));
            });

            //添加验证服务
            services.AddAuthentication(option =>
            {
                //默认身份验证模式
                option.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                //默认方案
                option.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                option.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

            }).AddJwtBearer(option =>
            {
                //设置元数据地址或权限是否需要HTTP
                option.RequireHttpsMetadata = false;
                option.SaveToken = true;
                //令牌验证参数
                option.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    //获取或设置要使用的Microsoft.IdentityModel.Tokens.SecurityKey用于签名验证。
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.
                    GetBytes(JWTTokenOptions.Secret)),
                    //获取或设置一个System.String，它表示将使用的有效发行者检查代币的发行者。 
                    ValidIssuer = JWTTokenOptions.Issuer,
                    //获取或设置一个字符串，该字符串表示将用于检查的有效受众反对令牌的观众。
                    ValidAudience = JWTTokenOptions.Audience,
                    //是否验证发起人
                    ValidateIssuer = false,
                    //是否验证订阅者
                    ValidateAudience = false,
                    ////允许的服务器时间偏移量
                    ClockSkew = TimeSpan.Zero,
                    ////是否验证Token有效期，使用当前时间与Token的Claims中的NotBefore和Expires对比
                    ValidateLifetime = true
                };
                //如果jwt过期，在返回的header中加入Token-Expired字段为true，前端在获取返回header时判断
                option.Events = new JwtBearerEvents()
                {
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                        {
                            context.Response.Headers.Add("Token-Expired", "true");
                        }
                        return Task.CompletedTask;
                    }
                };
            });
            #endregion

            #region redis缓存
            var redisconfig = _configHelper.Load("redisconfig", _env.EnvironmentName, true);
            //redis缓存
            var section = redisconfig.GetSection("Redis:Default");
            //连接字符串
            ConfigHelperRedis._conn = section.GetSection("Connection").Value;

            //实例化名称
            ConfigHelperRedis._name = section.GetSection("InstanceName").Value;
            //默认数据库
            ConfigHelperRedis._db = int.Parse(section.GetSection("DefaultDB").Value ?? "0");

            services.AddSingleton(new RedisHelper());
            #endregion

            #region Core跨域请求
            var corsconfig = _configHelper.Load("corsconfig", _env.EnvironmentName, true);
            services.AddCors(c =>
            {
                c.AddPolicy("CorsPolicy", policy =>
                {
                    //var corsPath = corsconfig.GetSection("CorsPaths").GetChildren().Select(p => p.Value).ToArray();
                    policy
                    //.WithOrigins(corsPath)//开发结束后开启客户端访问跨域限制
                    .AllowAnyOrigin()
                    //.AllowAnyMethod()
                    .WithMethods("GET", "POST", "PUT", "DELETE")
                    .AllowAnyHeader()
                    //.AllowCredentials()//指定处理cookie  *与AllowAnyOrigin() 不支持同时使用
                    .SetPreflightMaxAge(TimeSpan.FromSeconds(60));
                    //如果接口已验证过一次跨域，则在60分钟内再次请求时，将不需要验证跨域
                });
            });
            #endregion

            #region SQL注入过滤器
            //控制器上加SQL注入过滤器
            services.AddControllers(options =>
            {
                options.Filters.Add<CustomSQLInjectFilter>();
            });
            #endregion

            #region 限流配置
            //加载配置
            services.AddOptions();
            //services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_3_0);//设置兼容性版本
            services.AddMemoryCache();

            //加载IpRateLimiting配置
            var ratelimitconfig = _configHelper.Load("ratelimitconfig", _env.EnvironmentName, true);
            services.Configure<IpRateLimitOptions>(ratelimitconfig.GetSection("IpRateLimiting"));

            //加载ip限制定制策略，即针对某一个ip的特殊限制，可选择性注释
            //services.Configure<IpRateLimitPolicies>(configuration.GetSection("IpRateLimitPolicies"));

            //注入计数器和规则存储
            services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
            services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
            //添加框架服务
            services.AddMvc();
            // clientId / clientIp解析器使用它。
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            //配置（计数器密钥生成器）
            services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
            #endregion

            #region AutoMapper 自动映射

            var serviceAssembly = Assembly.Load("ExpressCode.Services");
            services.AddAutoMapper(serviceAssembly);

            #endregion AutoMapper 自动映射

            #region Json原样输出
            //在Startup类的ConfigureServices()方法中进行配置，DefaultContractResolver() 原样输出，返回的 json 与后台定义一致
            services.AddControllers().AddJsonOptions(option => option.JsonSerializerOptions.PropertyNamingPolicy = null);
            #endregion


            services.AddControllers().AddControllersAsServices();
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "ExpressCode.Api", Version = "v1" });
            });


        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            #region Nlog
            //添加NLog
            loggerFactory.AddNLog();
            //加载配置
            NLog.LogManager.LoadConfiguration("NLog.config");
            //调用自定义的中间件
            app.UseLog();
            #endregion

            #region 环境判断
            //用于判断是开发环境还是运行环境，保护开发环境和正式环境的间隔
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ExpressCode.Api v1"));
            }
            #endregion

            #region 添加中间件
            app.UseStaticFiles();//提供对静态文件的浏览

            app.UseRouting(); //配置http请求路由

            app.UseIpRateLimiting();//ip访问限流中间件

            app.UseAuthentication();// 认证

            app.UseAuthorization();// 授权

            app.UseCors("CorsPolicy");//开启Cors跨域请求中间件

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            #endregion

        }


        public void ConfigureContainer(ContainerBuilder builder)
        {
            //单个接口注入
            //builder.RegisterType<UserService>().As<IUserService>().InstancePerLifetimeScope();

            #region 注册AOP 
            builder.RegisterType<AopTest>();     //注册AOP
            #endregion

            #region 批量注入
            var services = Assembly.Load("ExpressCode.Services");
            var repository = Assembly.Load("ExpressCode.Repository");
            builder.RegisterAssemblyTypes(services, repository)
                .Where(t => t.Name.EndsWith("Service") || t.Name.EndsWith("Repository"))
                .AsImplementedInterfaces()
                .EnableInterfaceInterceptors()      //允许AOP,开启拦截
                .InstancePerLifetimeScope()   //同一个Lifetime生成的对象是同一个实例
                .PropertiesAutowired();   //属性注入
            #endregion

            // InstancePerLifetimeScope 同一个Lifetime生成的对象是同一个实例
            // SingleInstance 单例模式，每次调用，都会使用同一个实例化的对象；每次都用同一个对象
            // InstancePerDependency 默认模式，每次调用，都会重新实例化对象；每次请求都创建一个新的对象
        }
    }
}
