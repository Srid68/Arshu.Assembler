# Build stage
FROM php:8.2-apache AS build
RUN apt-get update && apt-get install -y curl gnupg2 && rm -rf /var/lib/apt/lists/*
RUN curl -L https://github.com/DarthSim/overmind/releases/download/v2.4.0/overmind-v2.4.0-linux-amd64.gz | gunzip -c > /usr/local/bin/overmind && chmod +x /usr/local/bin/overmind

# Enable mod_rewrite for Slim (in final stage)

WORKDIR /src
COPY . /src

# Install Composer
RUN curl -sS https://getcomposer.org/installer | php -- --install-dir=/usr/local/bin --filename=composer
# Install dependencies in AssemblerWeb if composer.json exists
RUN if [ -f /src/AssemblerWeb/composer.json ]; then cd /src/AssemblerWeb && composer install --no-dev --optimize-autoloader; fi
# Install dependencies in Assembler if composer.json exists
RUN if [ -f /src/Assembler/composer.json ]; then cd /src/Assembler && composer install --no-dev --optimize-autoloader; fi

# Runtime stage
FROM php:8.2-apache AS final
RUN apt-get update && apt-get install -y tmux curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /usr/local/bin/overmind /usr/local/bin/overmind

WORKDIR /app
COPY --from=build /src/AssemblerWeb /app/AssemblerWeb
COPY --from=build /src/Assembler /app/Assembler

# Set permissions
RUN chown -R www-data:www-data /app
RUN chmod +x /app/AssemblerWeb/IdleTrackingMonitor.php

RUN sed -i 's/Listen 80/Listen 8080/' /etc/apache2/ports.conf
RUN sed -i 's/:80/:8080/' /etc/apache2/sites-enabled/000-default.conf
# Set DocumentRoot to /app/AssemblerWeb in both configs
RUN sed -i 's|DocumentRoot /var/www/html|DocumentRoot /app/AssemblerWeb|' /etc/apache2/sites-enabled/000-default.conf
RUN sed -i 's|DocumentRoot /var/www/html|DocumentRoot /app/AssemblerWeb|' /etc/apache2/apache2.conf
# Relax directory permissions
RUN sed -i 's/AllowOverride None/AllowOverride All/g' /etc/apache2/apache2.conf
RUN sed -i 's/Require all denied/Require all granted/g' /etc/apache2/apache2.conf
RUN a2enmod rewrite
EXPOSE 8080
COPY AssemblerWeb/Procfile /app/Procfile
CMD ["/bin/sh", "-c", "rm -f /app/.overmind.sock; /usr/local/bin/overmind start -f ./Procfile"]
