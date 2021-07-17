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

            #region ���ݿ�����

            var dbConfig = new ConfigHelper().Get<DbConfig>("dbconfig", _env.EnvironmentName);
            DBFactory.ConnectDB = dbConfig.UseDB;//�ڴ��л����ݿ�����
            DBConfigHelper.ConnectSqlServer = dbConfig.SqlServerConStr;
            DBConfigHelper.ConnectMySql = dbConfig.MySqlConstr;

            #endregion

            #region ע��jwt
            JWTTokenOptions JWTTokenOptions = new JWTTokenOptions();
            //��ȡappsettings������
            JWTTokenOptions = new ConfigHelper().Get<JWTTokenOptions>("jwtconfig", _env.EnvironmentName);
            //�������Ķ���ʵ���󶨵�ָ�������ýڵ�
            Configuration.Bind("JWTToken", JWTTokenOptions);

            //ע�ᵽIoc����
            services.AddSingleton(JWTTokenOptions);

            //����Ȩ��
            services.AddAuthorization(options =>
            {
                options.AddPolicy("Client", policy => policy.RequireRole("Client").Build());
                options.AddPolicy("Admin", policy => policy.RequireRole("Admin").Build());
                options.AddPolicy("SystemOrAdmin", policy => policy.RequireRole("Admin", "System"));
            });

            //�����֤����
            services.AddAuthentication(option =>
            {
                //Ĭ�������֤ģʽ
                option.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                //Ĭ�Ϸ���
                option.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                option.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

            }).AddJwtBearer(option =>
            {
                //����Ԫ���ݵ�ַ��Ȩ���Ƿ���ҪHTTP
                option.RequireHttpsMetadata = false;
                option.SaveToken = true;
                //������֤����
                option.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    //��ȡ������Ҫʹ�õ�Microsoft.IdentityModel.Tokens.SecurityKey����ǩ����֤��
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.
                    GetBytes(JWTTokenOptions.Secret)),
                    //��ȡ������һ��System.String������ʾ��ʹ�õ���Ч�����߼����ҵķ����ߡ� 
                    ValidIssuer = JWTTokenOptions.Issuer,
                    //��ȡ������һ���ַ��������ַ�����ʾ�����ڼ�����Ч���ڷ������ƵĹ��ڡ�
                    ValidAudience = JWTTokenOptions.Audience,
                    //�Ƿ���֤������
                    ValidateIssuer = false,
                    //�Ƿ���֤������
                    ValidateAudience = false,
                    ////����ķ�����ʱ��ƫ����
                    ClockSkew = TimeSpan.Zero,
                    ////�Ƿ���֤Token��Ч�ڣ�ʹ�õ�ǰʱ����Token��Claims�е�NotBefore��Expires�Ա�
                    ValidateLifetime = true
                };
                //���jwt���ڣ��ڷ��ص�header�м���Token-Expired�ֶ�Ϊtrue��ǰ���ڻ�ȡ����headerʱ�ж�
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

            #region redis����
            var redisconfig = _configHelper.Load("redisconfig", _env.EnvironmentName, true);
            //redis����
            var section = redisconfig.GetSection("Redis:Default");
            //�����ַ���
            ConfigHelperRedis._conn = section.GetSection("Connection").Value;

            //ʵ��������
            ConfigHelperRedis._name = section.GetSection("InstanceName").Value;
            //Ĭ�����ݿ�
            ConfigHelperRedis._db = int.Parse(section.GetSection("DefaultDB").Value ?? "0");

            services.AddSingleton(new RedisHelper());
            #endregion

            #region Core��������
            var corsconfig = _configHelper.Load("corsconfig", _env.EnvironmentName, true);
            services.AddCors(c =>
            {
                c.AddPolicy("CorsPolicy", policy =>
                {
                    //var corsPath = corsconfig.GetSection("CorsPaths").GetChildren().Select(p => p.Value).ToArray();
                    policy
                    //.WithOrigins(corsPath)//�������������ͻ��˷��ʿ�������
                    .AllowAnyOrigin()
                    //.AllowAnyMethod()
                    .WithMethods("GET", "POST", "PUT", "DELETE")
                    .AllowAnyHeader()
                    //.AllowCredentials()//ָ������cookie  *��AllowAnyOrigin() ��֧��ͬʱʹ��
                    .SetPreflightMaxAge(TimeSpan.FromSeconds(60));
                    //����ӿ�����֤��һ�ο�������60�������ٴ�����ʱ��������Ҫ��֤����
                });
            });
            #endregion

            #region SQLע�������
            //�������ϼ�SQLע�������
            services.AddControllers(options =>
            {
                options.Filters.Add<CustomSQLInjectFilter>();
            });
            #endregion

            #region ��������
            //��������
            services.AddOptions();
            //services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_3_0);//���ü����԰汾
            services.AddMemoryCache();

            //����IpRateLimiting����
            var ratelimitconfig = _configHelper.Load("ratelimitconfig", _env.EnvironmentName, true);
            services.Configure<IpRateLimitOptions>(ratelimitconfig.GetSection("IpRateLimiting"));

            //����ip���ƶ��Ʋ��ԣ������ĳһ��ip���������ƣ���ѡ����ע��
            //services.Configure<IpRateLimitPolicies>(configuration.GetSection("IpRateLimitPolicies"));

            //ע��������͹���洢
            services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
            services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
            //��ӿ�ܷ���
            services.AddMvc();
            // clientId / clientIp������ʹ������
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            //���ã���������Կ��������
            services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
            #endregion

            #region AutoMapper �Զ�ӳ��

            var serviceAssembly = Assembly.Load("ExpressCode.Services");
            services.AddAutoMapper(serviceAssembly);

            #endregion AutoMapper �Զ�ӳ��

            #region Jsonԭ�����
            //��Startup���ConfigureServices()�����н������ã�DefaultContractResolver() ԭ����������ص� json ���̨����һ��
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
            //���NLog
            loggerFactory.AddNLog();
            //��������
            NLog.LogManager.LoadConfiguration("NLog.config");
            //�����Զ�����м��
            app.UseLog();
            #endregion

            #region �����ж�
            //�����ж��ǿ��������������л���������������������ʽ�����ļ��
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ExpressCode.Api v1"));
            }
            #endregion

            #region ����м��
            app.UseStaticFiles();//�ṩ�Ծ�̬�ļ������

            app.UseRouting(); //����http����·��

            app.UseIpRateLimiting();//ip���������м��

            app.UseAuthentication();// ��֤

            app.UseAuthorization();// ��Ȩ

            app.UseCors("CorsPolicy");//����Cors���������м��

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            #endregion

        }


        public void ConfigureContainer(ContainerBuilder builder)
        {
            //�����ӿ�ע��
            //builder.RegisterType<UserService>().As<IUserService>().InstancePerLifetimeScope();

            #region ע��AOP 
            builder.RegisterType<AopTest>();     //ע��AOP
            #endregion

            #region ����ע��
            var services = Assembly.Load("ExpressCode.Services");
            var repository = Assembly.Load("ExpressCode.Repository");
            builder.RegisterAssemblyTypes(services, repository)
                .Where(t => t.Name.EndsWith("Service") || t.Name.EndsWith("Repository"))
                .AsImplementedInterfaces()
                .EnableInterfaceInterceptors()      //����AOP,��������
                .InstancePerLifetimeScope()   //ͬһ��Lifetime���ɵĶ�����ͬһ��ʵ��
                .PropertiesAutowired();   //����ע��
            #endregion

            // InstancePerLifetimeScope ͬһ��Lifetime���ɵĶ�����ͬһ��ʵ��
            // SingleInstance ����ģʽ��ÿ�ε��ã�����ʹ��ͬһ��ʵ�����Ķ���ÿ�ζ���ͬһ������
            // InstancePerDependency Ĭ��ģʽ��ÿ�ε��ã���������ʵ��������ÿ�����󶼴���һ���µĶ���
        }
    }
}
